import { useState, useRef, useEffect, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';

interface UseGamepadConnectionProps {
  serverUrl: string;
  serverPassword?: string;
  controllerType: number;
  enableVib: boolean;
  sensitivity: number;
  onError?: (message: string) => void;
}

export const useGamepadConnection = ({
  serverUrl, serverPassword = '', controllerType, enableVib, sensitivity, onError
}: UseGamepadConnectionProps) => {
  const [connStatus, setConnStatus] = useState<'off' | 'ing' | 'on'>('off');
  const [latency, setLatency] = useState<number | null>(null);
  const [controllerId, setControllerId] = useState<number | null>(null);

  const connRef = useRef<signalR.HubConnection | null>(null);
  const pcRef = useRef<RTCPeerConnection | null>(null);
  const dcRef = useRef<RTCDataChannel | null>(null);
  const pingRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const authFailedRef = useRef(false);
  const notifyError = useCallback((msg: string) => {
    if (onError) onError(msg); else alert(msg);
  }, [onError]);
  const manualDisconnectRef = useRef(false);
  const reconnectAttemptsRef = useRef(0);
  const reconnectTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const connectRef = useRef<() => Promise<void>>(async () => {});
  const pendingJoyRef = useRef<{ stick: string; x: number; y: number } | null>(null);
  const joyRafRef = useRef<number | null>(null);

  const updateLiveSetting = useCallback((key: string, val: string) => {
    if (connStatus === 'on' && connRef.current?.state === signalR.HubConnectionState.Connected) {
      connRef.current.invoke('UpdateSetting', key, val).catch(console.error);
    }
  }, [connStatus]);

  useEffect(() => { updateLiveSetting('ControllerType', controllerType.toString()); }, [controllerType, updateLiveSetting]);
  useEffect(() => { updateLiveSetting('EnableVib', enableVib.toString()); }, [enableVib, updateLiveSetting]);
  useEffect(() => { updateLiveSetting('Sensitivity', sensitivity.toString()); }, [sensitivity, updateLiveSetting]);

  useEffect(() => {
    return () => {
      if (joyRafRef.current != null) cancelAnimationFrame(joyRafRef.current);
      if (reconnectTimerRef.current) clearTimeout(reconnectTimerRef.current);
      if (pingRef.current) clearInterval(pingRef.current);
      dcRef.current?.close();
      pcRef.current?.close();
      connRef.current?.stop();
    };
  }, []);

  const scheduleReconnect = useCallback(() => {
    if (manualDisconnectRef.current) return;
    if (reconnectTimerRef.current) return;
    if (reconnectAttemptsRef.current >= 3) return;
    reconnectAttemptsRef.current += 1;
    const delay = 1000 * reconnectAttemptsRef.current;
    reconnectTimerRef.current = setTimeout(() => {
      reconnectTimerRef.current = null;
      connectRef.current?.();
    }, delay);
  }, []);

  const connect = useCallback(async () => {
    manualDisconnectRef.current = false;
    if (reconnectTimerRef.current) { clearTimeout(reconnectTimerRef.current); reconnectTimerRef.current = null; }
    reconnectAttemptsRef.current = 0;
    if (pingRef.current) { clearInterval(pingRef.current); pingRef.current = null; }
    if (dcRef.current) { dcRef.current.close(); dcRef.current = null; }
    if (pcRef.current) { pcRef.current.close(); pcRef.current = null; }
    if (connRef.current) { await connRef.current.stop(); connRef.current = null; }
    setConnStatus('ing');
    setLatency(null);
    setControllerId(null);
    authFailedRef.current = false;

    let url = serverUrl.trim().replace(/\/$/, '');
    if (!url.endsWith('/gamepadhub')) url += '/gamepadhub';

    const c = new signalR.HubConnectionBuilder()
      .withUrl(url, { skipNegotiation: true, transport: signalR.HttpTransportType.WebSockets })
      .build();
    c.onclose(() => {
      if (dcRef.current?.readyState !== 'open') setConnStatus('off');
      scheduleReconnect();
    });

    const pc = new RTCPeerConnection();
    pcRef.current = pc;

    const dc = pc.createDataChannel('gamepad', { ordered: false, maxRetransmits: 0 });
    dcRef.current = dc;

    dc.onopen = () => {
      console.log('[WebRTC] Data Channel opened — ready to send gamepad data!');
      reconnectAttemptsRef.current = 0;
      setConnStatus('on');
      pingRef.current = setInterval(() => {
        if (dcRef.current?.readyState === 'open') {
          dcRef.current.send(`Ping:${Date.now()}`);
        }
      }, 2000);
    };
    dc.onclose = () => {
      console.log('[WebRTC] Data Channel closed');
      if (pingRef.current) { clearInterval(pingRef.current); pingRef.current = null; }
      setLatency(null);
      setControllerId(null);
      setConnStatus('off');
      scheduleReconnect();
    };
    dc.onmessage = (event) => {
      const msg = typeof event.data === 'string' ? event.data : '';
      if (msg.startsWith('Pong:')) {
        const sent = parseInt(msg.substring(5), 10);
        if (!isNaN(sent)) setLatency(Date.now() - sent);
      } else {
        const parts = msg.split(':');
        if (parts[0] === 'V') {
          const intensity = parseInt(parts[1], 10);
          if (intensity > 0) {
            if (navigator.vibrate) navigator.vibrate(100);
          } else {
            if (navigator.vibrate) navigator.vibrate(0);
          }
        } else if (parts[0] === 'ID') {
          const id = parseInt(parts[1], 10);
          if (!isNaN(id)) setControllerId(id);
        }
      }
    };

    pc.onicecandidate = (event) => {
      if (event.candidate && connRef.current?.state === signalR.HubConnectionState.Connected) {
        connRef.current.invoke('SendIceCandidate', JSON.stringify(event.candidate.toJSON())).catch(console.error);
      }
    };

    pc.onconnectionstatechange = () => {
      if (pc.connectionState === 'failed' || pc.connectionState === 'disconnected' || pc.connectionState === 'closed') {
        console.log('[WebRTC] Connection state:', pc.connectionState);
        setConnStatus('off');
        setLatency(null);
        scheduleReconnect();
      }
    };

    c.on('ReceiveAnswer', async (sdpAnswer: string) => {
      try { await pc.setRemoteDescription({ type: 'answer', sdp: sdpAnswer }); }
      catch (err) { console.error('[WebRTC] Failed to set remote description:', err); }
    });
    c.on('ReceiveIceCandidate', async (iceCandidateJson: string) => {
      try { await pc.addIceCandidate(JSON.parse(iceCandidateJson)); }
      catch (err) { console.error('[WebRTC] Failed to add ICE candidate:', err); }
    });

    c.on('AuthFailed', (msg: string) => {
      authFailedRef.current = true;
      notifyError(`Connection failed: ${msg === 'Invalid server password.' ? 'Invalid server password.' : msg}`);
    });

    try {
      await c.start();
      connRef.current = c;
      // WebRTC data channel is not open yet; keep "connecting" until dc.onopen fires.
      setConnStatus('ing');

      const offer = await pc.createOffer();
      await pc.setLocalDescription(offer);
      await c.invoke('SendOffer', offer.sdp, controllerType, enableVib, sensitivity, serverPassword);
    } catch (err) {
      console.error(err);
      pc.close(); pcRef.current = null;
      dc.close(); dcRef.current = null;
      setConnStatus('off');
      if (!authFailedRef.current) {
        notifyError('Connection failed: ' + err);
      }
    }
  }, [serverUrl, serverPassword, controllerType, enableVib, sensitivity, scheduleReconnect, notifyError]);

  useEffect(() => { connectRef.current = connect; }, [connect]);

  const disconnect = async () => {
    manualDisconnectRef.current = true;
    if (joyRafRef.current != null) { cancelAnimationFrame(joyRafRef.current); joyRafRef.current = null; }
    if (reconnectTimerRef.current) { clearTimeout(reconnectTimerRef.current); reconnectTimerRef.current = null; }
    if (pingRef.current) { clearInterval(pingRef.current); pingRef.current = null; }
    setLatency(null);
    setControllerId(null);
    if (dcRef.current) { dcRef.current.close(); dcRef.current = null; }
    if (pcRef.current) { pcRef.current.close(); pcRef.current = null; }
    await connRef.current?.stop(); connRef.current = null;
    setConnStatus('off');
  };

  const sendButton = useCallback((btn: string, pressed: boolean) => {
    const dc = dcRef.current;
    if (dc?.readyState === 'open') {
      dc.send(`B:${btn}:${pressed ? '1' : '0'}`);
    }
  }, []);

  // Batch joystick updates to one frame to reduce packet rate.

  const flushJoy = useCallback(() => {
    joyRafRef.current = null;
    const p = pendingJoyRef.current;
    pendingJoyRef.current = null;
    const dc = dcRef.current;
    if (!p || dc?.readyState !== 'open') return;
    // Binary protocol: [0x02][stick: 0=Left,1=Right][x int16 LE][y int16 LE]
    const buf = new ArrayBuffer(6);
    const v = new DataView(buf);
    v.setUint8(0, 0x02);
    v.setUint8(1, p.stick === 'Left' ? 0 : 1);
    v.setInt16(2, p.x, true);
    v.setInt16(4, p.y, true);
    dc.send(buf);
  }, []);

  const sendJoystick = useCallback((stick: string, x: number, y: number) => {
    pendingJoyRef.current = { stick, x, y };
    if (joyRafRef.current == null) {
      joyRafRef.current = requestAnimationFrame(flushJoy);
    }
  }, [flushJoy]);

  return { connStatus, latency, controllerId, connect, disconnect, sendButton, sendJoystick };
};
