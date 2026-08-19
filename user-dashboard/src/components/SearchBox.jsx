import { useEffect, useRef, useState } from 'react';
import Icon from './Icon.jsx';

/**
 * Prominent, reusable search / question input with a submit button.
 */
export default function SearchBox({
  value,
  onChange,
  onSubmit,
  placeholder = '',
  submitLabel = 'Search',
  busy = false,
  autoFocus = false,
  size = 'large'
}) {
  const [internal, setInternal] = useState(value ?? '');
  const inputRef = useRef(null);
  const controlled = value !== undefined && onChange !== undefined;
  const current = controlled ? value : internal;

  useEffect(() => {
    if (autoFocus) inputRef.current?.focus();
  }, [autoFocus]);

  function handleChange(event) {
    if (controlled) onChange(event.target.value);
    else setInternal(event.target.value);
  }

  function handleSubmit(event) {
    event.preventDefault();
    onSubmit?.(current.trim());
  }

  return (
    <form className={`search-box search-box-${size}`} onSubmit={handleSubmit} role="search">
      <span className="search-box-icon" aria-hidden="true">
        <Icon name="search" size={size === 'large' ? 20 : 16} />
      </span>
      <input
        ref={inputRef}
        type="search"
        className="search-box-input"
        value={current}
        onChange={handleChange}
        placeholder={placeholder}
        autoComplete="off"
        spellCheck="false"
      />
      <button type="submit" className="button button-primary search-box-submit" disabled={busy || !current.trim()}>
        {submitLabel}
      </button>
    </form>
  );
}
