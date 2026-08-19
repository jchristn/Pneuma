import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Icon from './Icon.jsx';

async function copyToClipboard(text) {
  try {
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {
    /* fall through to legacy path */
  }
  try {
    const textarea = document.createElement('textarea');
    textarea.value = text;
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.select();
    const ok = document.execCommand('copy');
    document.body.removeChild(textarea);
    return ok;
  } catch {
    return false;
  }
}

export default function CopyButton({ text, label, className = '' }) {
  const { t } = useTranslation();
  const [copied, setCopied] = useState(false);
  const timerRef = useRef(null);
  const hasValue = text !== undefined && text !== null && String(text).length > 0;
  const title = copied ? t('common.copied') : label || t('common.copy');

  useEffect(
    () => () => {
      if (timerRef.current) window.clearTimeout(timerRef.current);
    },
    []
  );

  async function handleClick(event) {
    event.preventDefault();
    event.stopPropagation();
    if (!hasValue) return;
    const ok = await copyToClipboard(String(text));
    if (!ok) return;
    setCopied(true);
    if (timerRef.current) window.clearTimeout(timerRef.current);
    timerRef.current = window.setTimeout(() => setCopied(false), 1400);
  }

  return (
    <button
      type="button"
      className={`copy-button${copied ? ' is-copied' : ''}${className ? ` ${className}` : ''}`}
      onClick={handleClick}
      aria-label={title}
      title={title}
      disabled={!hasValue}
    >
      <Icon name={copied ? 'check' : 'copy'} size={14} />
    </button>
  );
}
