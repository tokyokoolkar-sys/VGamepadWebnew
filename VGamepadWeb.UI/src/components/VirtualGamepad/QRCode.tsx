import React, { useEffect, useState } from 'react';

export const QRCodeImage: React.FC<{ value: string; size?: number }> = ({ value, size = 160 }) => {
  const [src, setSrc] = useState<string>('');

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const QRCode = await import('qrcode');
        const url = await QRCode.toDataURL(value, { width: size, margin: 1 });
        if (!cancelled) setSrc(url);
      } catch {
        if (!cancelled) setSrc('');
      }
    })();
    return () => { cancelled = true; };
  }, [value, size]);

  if (!src) return null;
  return <img src={src} alt="QR code" width={size} height={size} style={{ borderRadius: '8px', background: '#fff', padding: '4px' }} />;
};