// Rows of count cards above a DataGrid. Each row has a label and up to `columns` cards laid out on a fixed grid,
// so the cards never wrap into one long ragged strip. A card with onClick acts as a filter for the grid below
// (active marks the filter that is on). Kept identical in the admin and subject dashboards.
function SummaryCards({ rows, columns = 5 }) {
  return (
    <div className="dg-cards">
      {rows.map((row) => (
        <div className="dg-card-row" key={row.key} style={{ '--dg-cols': columns }} role="group" aria-label={row.label}>
          <span className="dg-card-row-label">{row.label}</span>
          {row.cards.map((card) => {
            const body = (
              <>
                <span className="dg-card-value">{typeof card.value === 'number' ? card.value.toLocaleString() : card.value}</span>
                <span className="dg-card-label" title={card.tip || card.label}>{card.label}</span>
              </>
            );
            const cls = `dg-card${card.active ? ' on' : ''}${card.danger && Number(card.value) > 0 ? ' danger' : ''}`;
            return card.onClick ? (
              <button type="button" key={card.key} className={cls} onClick={card.onClick} aria-pressed={!!card.active}
                title={card.tip || card.label}>{body}</button>
            ) : (
              <div key={card.key} className={cls}>{body}</div>
            );
          })}
        </div>
      ))}
    </div>
  );
}

export default SummaryCards;
