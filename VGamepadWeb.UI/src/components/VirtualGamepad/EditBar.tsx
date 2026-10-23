import React from 'react';
import type { GamepadLayout, ThemeType } from './types';
import { STICK_IDS, THEMES } from './constants';
import { useLanguage } from './LanguageContext';

interface EditBarProps {
  editMode: boolean;
  selectedControl: string | null;
  layout: GamepadLayout;
  theme: ThemeType;
  updateSize: (id: string, delta: number) => void;
  setSize: (id: string, size: number) => void;
  setShape: (id: string, round: boolean) => void;
  nudgePos: (id: string, dx: number, dy: number) => void;
  resetControl: (id: string) => void;
  setSelectedControl: (id: string | null) => void;
  updateLabel?: (id: string, label: string) => void;
  updateColor?: (id: string, color: string) => void;
  onAddButton?: () => void;
}

export const EditBar: React.FC<EditBarProps> = ({
  editMode, selectedControl, layout, theme, updateSize, setSize, setShape, nudgePos, resetControl, setSelectedControl,
  updateLabel, updateColor, onAddButton
}) => {
  const { t } = useLanguage();

  if (!editMode) return null;

  const getSelectedSize = () => {
    if (!selectedControl) return 0;
    const ctrl = layout.controls[selectedControl];
    const isStick = (STICK_IDS as readonly string[]).includes(selectedControl);
    const defaultW = isStick ? 120 : (THEMES[theme][selectedControl]?.w ?? 56);
    return ctrl?.w ?? defaultW;
  };

  const getSelectedRound = () => {
    if (!selectedControl || !THEMES[theme][selectedControl]) return true;
    const ctrl = layout.controls[selectedControl];
    return ctrl?.round ?? THEMES[theme][selectedControl]?.round ?? true;
  };

  const isCustom = !!selectedControl && !THEMES[theme][selectedControl] && !(STICK_IDS as readonly string[]).includes(selectedControl);

  return (
    <div className="gp-edit-bar">
      {selectedControl ? (
        <div className="gp-edit-panel">
          <div className="gp-edit-header">
            <span className="gp-edit-icon">
              {(STICK_IDS as readonly string[]).includes(selectedControl) ? '🕹️' : '🔘'}
            </span>
            <span className="gp-edit-title">
              {selectedControl === 'LeftStick' ? t.leftStickFull : 
               selectedControl === 'RightStick' ? t.rightStickFull : 
               `${t.buttonPrefix} ${selectedControl}`}
            </span>
          </div>

          <div className="gp-edit-body">
            {/* Size Control Row */}
            <div className="gp-edit-section">
              <span className="gp-edit-label">{t.size}</span>
              <div className="gp-edit-slider-wrap">
                <button className="gp-edit-btn-val" onClick={() => updateSize(selectedControl, -4)}>−</button>
                <input 
                  type="range" 
                  min="24" 
                  max="200" 
                  value={getSelectedSize()} 
                  onChange={(e) => setSize(selectedControl, parseInt(e.target.value, 10))} 
                  className="gp-edit-slider"
                />
                <button className="gp-edit-btn-val" onClick={() => updateSize(selectedControl, 4)}>+</button>
                <span className="gp-edit-value-badge">{getSelectedSize()}px</span>
              </div>
            </div>

            {/* Shape Control Row (Only for buttons) */}
            {THEMES[theme][selectedControl] && (
              <div className="gp-edit-section">
                <span className="gp-edit-label">{t.shape}</span>
                <div className="gp-edit-segmented">
                  <button 
                    className={`gp-edit-seg-btn ${getSelectedRound() ? 'active' : ''}`}
                    onClick={() => setShape(selectedControl, true)}
                  >
                    {t.circular}
                  </button>
                  <button 
                    className={`gp-edit-seg-btn ${!getSelectedRound() ? 'active' : ''}`}
                    onClick={() => setShape(selectedControl, false)}
                  >
                    {t.rectangle}
                  </button>
                </div>
              </div>
            )}

            {/* Custom button label/color (only for custom buttons) */}
            {isCustom && (
              <>
                <div className="gp-edit-section">
                  <span className="gp-edit-label">Label</span>
                  <input
                    type="text"
                    className="gp-input"
                    value={layout.controls[selectedControl!]?.label ?? ''}
                    onChange={e => updateLabel?.(selectedControl!, e.target.value)}
                    maxLength={4}
                    style={{ marginBottom: '8px' }}
                  />
                </div>
                <div className="gp-edit-section">
                  <span className="gp-edit-label">Color</span>
                  <input
                    type="color"
                    value={layout.controls[selectedControl!]?.color ?? '#64748b'}
                    onChange={e => updateColor?.(selectedControl!, e.target.value)}
                    style={{ width: '100%', height: '32px', marginBottom: '8px' }}
                  />
                </div>
              </>
            )}

            {/* Fine Nudge Control Row */}
            <div className="gp-edit-section">
              <span className="gp-edit-label">{t.position}</span>
              <div className="gp-edit-nudge">
                <button className="gp-nudge-btn up" onClick={() => nudgePos(selectedControl, 0, -1)}>▲</button>
                <div className="gp-nudge-mid">
                  <button className="gp-nudge-btn left" onClick={() => nudgePos(selectedControl, -1, 0)}>◀</button>
                  <span className="gp-nudge-title">{t.align}</span>
                  <button className="gp-nudge-btn right" onClick={() => nudgePos(selectedControl, 1, 0)}>▶</button>
                </div>
                <button className="gp-nudge-btn down" onClick={() => nudgePos(selectedControl, 0, 1)}>▼</button>
              </div>
            </div>
          </div>

          <div className="gp-edit-actions">
            <button className="gp-edit-action reset" onClick={() => resetControl(selectedControl)}>
              {t.resetButton}
            </button>
            <button className="gp-edit-action close" onClick={() => setSelectedControl(null)}>
              {t.closePanel}
            </button>
          </div>
        </div>
      ) : (
        <div className="gp-edit-hint">
          <span className="gp-edit-hint-pulse"></span>
          <span className="gp-edit-hint-text">{t.editHint}</span>
          {onAddButton && (
            <button className="gp-mbtn green" onClick={onAddButton} style={{ marginTop: '8px' }}>
              + Add Button
            </button>
          )}
        </div>
      )}
    </div>
  );
};
