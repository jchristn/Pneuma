import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { nodeDisplayName } from '../utils/nodes.js';

/**
 * Card representing a graph node in a result / source list.
 * Shows the name, a node-type pill, an optional snippet, and an optional score.
 * The whole card is clickable and keyboard-activatable, navigating to the node.
 */
export default function NodeCard({ node, snippet = null, score = null }) {
  const navigate = useNavigate();
  const { t } = useTranslation();
  if (!node) return null;

  const name = nodeDisplayName(node);
  const goToNode = () => navigate(`/node/${encodeURIComponent(node.id)}`);

  function handleKeyDown(event) {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      goToNode();
    }
  }

  const scoreValue = typeof score === 'number' ? score : null;

  return (
    <article
      className="node-card"
      role="button"
      tabIndex={0}
      onClick={goToNode}
      onKeyDown={handleKeyDown}
      aria-label={name}
    >
      <div className="node-card-head">
        {node.nodeType ? <span className="pill pill-type">{node.nodeType}</span> : null}
        {scoreValue !== null ? (
          <span className="node-card-score" title={t('common.score')}>
            {t('common.score')} {scoreValue.toFixed(2)}
          </span>
        ) : null}
      </div>
      <h3 className="node-card-title">{name}</h3>
      {snippet ? <p className="node-card-snippet">{snippet}</p> : null}
      {Array.isArray(node.labels) && node.labels.length > 0 ? (
        <div className="node-card-labels">
          {node.labels.slice(0, 4).map((label) => (
            <span key={label} className="pill pill-label">
              {label}
            </span>
          ))}
        </div>
      ) : null}
    </article>
  );
}
