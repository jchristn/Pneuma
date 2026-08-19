/**
 * Standard page header: title, subtitle, and right-aligned action slot.
 */
function PageHeader({ title, subtitle, actions }) {
  return (
    <div className="page-header">
      <div className="page-header-titles">
        <h1>{title}</h1>
        {subtitle && <p className="page-header-subtitle">{subtitle}</p>}
      </div>
      {actions && <div className="page-header-actions">{actions}</div>}
    </div>
  );
}

export default PageHeader;
