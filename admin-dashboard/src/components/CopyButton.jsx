import { useState, useCallback } from 'react';
import { useTranslation } from 'react-i18next';

/**
 * Copy text to the clipboard. Uses the async Clipboard API on secure origins
 * (https / localhost) and falls back to a hidden-textarea execCommand on insecure
 * origins (plain http on a non-localhost host), so copy works everywhere.
 */
function copyToClipboard(text) {
  if (navigator.clipboard && window.isSecureContext) {
    return navigator.clipboard.writeText(text);
  }
  return new Promise((resolve, reject) => {
    const area = document.createElement('textarea');
    area.value = text;
    area.style.position = 'fixed';
    area.style.top = '-1000px';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.focus();
    area.select();
    try {
      const ok = document.execCommand('copy');
      document.body.removeChild(area);
      if (ok) resolve(); else reject(new Error('copy failed'));
    } catch (err) {
      document.body.removeChild(area);
      reject(err);
    }
  });
}

function CopyButton({ value, label, className = '', title = null }) {
  const { t } = useTranslation();
  const [copied, setCopied] = useState(false);

  const handleCopy = useCallback(async (e) => {
    e.stopPropagation();
    try {
      await copyToClipboard(String(value ?? ''));
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      // clipboard unavailable
    }
  }, [value]);

  return (
    <button
      type="button"
      className={`copy-button ${copied ? 'copied' : ''} ${className}`}
      onClick={handleCopy}
      title={copied ? t('common.copied') : (title || label || t('common.copy'))}
      aria-label={copied ? t('common.copied') : (label || t('common.copy'))}
    >
      {copied ? '✓' : '⧉'}{label ? <span>{copied ? t('common.copied') : label}</span> : null}
    </button>
  );
}

export default CopyButton;
