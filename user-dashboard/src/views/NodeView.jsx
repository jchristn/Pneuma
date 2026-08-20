import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext.jsx';
import Icon from '../components/Icon.jsx';
import CopyButton from '../components/CopyButton.jsx';
import {
  describeEdge,
  extractLinks,
  labelsArray,
  nodeDisplayName,
  rightsTags,
  tagsObject
} from '../utils/nodes.js';

export default function NodeView() {
  const { id } = useParams();
  const navigate = useNavigate();
  const { t } = useTranslation();
  const { apiClient } = useAuth();

  const [node, setNode] = useState(null);
  const [neighbors, setNeighbors] = useState([]);
  const [edges, setEdges] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError('');
    setNode(null);

    (async () => {
      try {
        const [nodeData, neighborData, edgeData] = await Promise.all([
          apiClient.getNode(id),
          apiClient.getNeighbors(id).catch(() => []),
          apiClient.getEdges(id).catch(() => [])
        ]);
        if (cancelled) return;
        setNode(nodeData);
        setNeighbors(Array.isArray(neighborData) ? neighborData : []);
        setEdges(Array.isArray(edgeData) ? edgeData : []);
      } catch (err) {
        if (!cancelled) setError(err?.message || t('common.error'));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [apiClient, id, t]);

  const nodesById = useMemo(() => {
    const map = new Map();
    if (node) map.set(node.id, node);
    for (const neighbor of neighbors) {
      if (neighbor?.id) map.set(neighbor.id, neighbor);
    }
    return map;
  }, [node, neighbors]);

  const links = useMemo(() => extractLinks(node), [node]);
  const rights = useMemo(() => rightsTags(node), [node]);
  const labels = useMemo(() => labelsArray(node?.labels), [node]);
  const otherTags = useMemo(() => {
    const all = tagsObject(node?.tags);
    const rightsKeys = new Set(rights.map((r) => r.key));
    return Object.entries(all).filter(
      ([key, value]) => !rightsKeys.has(key) && !/url|link|href|uri/i.test(key) && value !== '' && value != null
    );
  }, [node, rights]);

  const backAffordance = (
    <button type="button" className="button button-ghost back-link" onClick={() => navigate(-1)}>
      <Icon name="arrowLeft" size={16} />
      <span>{t('nav.back', 'Back')}</span>
    </button>
  );

  if (loading) {
    return (
      <div className="view node-view">
        {backAffordance}
        <div className="state-block">
          <div className="spinner" />
          <p>{t('node.loading')}</p>
        </div>
      </div>
    );
  }

  if (error || !node) {
    return (
      <div className="view node-view">
        {backAffordance}
        <div className="state-block state-error">
          <Icon name="alert" size={28} />
          <h2>{t('node.notFoundTitle')}</h2>
          <p>{error || t('node.notFoundBody')}</p>
          <Link to="/" className="button button-secondary">
            {t('nav.ask', 'Ask')}
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="view node-view">
      {backAffordance}

      <header className="node-header">
        <div className="node-header-top">
          {node.nodeType ? <span className="pill pill-type">{node.nodeType}</span> : null}
          <span className="node-id mono">
            {node.id}
            <CopyButton text={node.id} label={t('node.copyId')} />
          </span>
        </div>
        <h1 className="node-title">{nodeDisplayName(node)}</h1>
        {node.canonicalName && node.canonicalName !== node.name ? (
          <p className="node-canonical mono">{node.canonicalName}</p>
        ) : null}
      </header>

      <div className="node-layout">
        <div className="node-main">
          {/* Contents */}
          <section className="panel">
            <h2 className="panel-title">{t('node.contentTitle')}</h2>
            {node.content ? (
              <p className="node-content-text">{node.content}</p>
            ) : (
              <p className="muted">{t('node.noContent')}</p>
            )}
          </section>

          {/* Links */}
          <section className="panel">
            <h2 className="panel-title">
              <Icon name="link" size={16} /> {t('node.linksTitle')}
            </h2>
            {links.length > 0 ? (
              <ul className="link-list">
                {links.map((link) => (
                  <li key={link.url}>
                    <a
                      href={link.url}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="external-link"
                      title={t('common.openInNewTab')}
                    >
                      <span className="external-link-text">{link.label}</span>
                      <Icon name="external" size={14} />
                    </a>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="muted">{t('node.noLinks')}</p>
            )}
          </section>

          {/* Relationships */}
          <section className="panel">
            <h2 className="panel-title">{t('node.relationshipsTitle')}</h2>
            {edges.length > 0 ? (
              <ul className="relationship-list">
                {edges.map((edge, index) => {
                  const { type, arrow, otherId, otherName } = describeEdge(edge, node.id, nodesById);
                  return (
                    <li key={edge?.id || index} className="relationship-item">
                      <span className="pill pill-edge">{type}</span>
                      <span className="relationship-arrow" aria-hidden="true">
                        {arrow}
                      </span>
                      {otherId ? (
                        <Link to={`/node/${encodeURIComponent(otherId)}`} className="relationship-target">
                          {otherName}
                        </Link>
                      ) : (
                        <span className="relationship-target">{otherName}</span>
                      )}
                    </li>
                  );
                })}
              </ul>
            ) : (
              <p className="muted">{t('node.noRelationships')}</p>
            )}
          </section>
        </div>

        <aside className="node-side">
          {/* Adjacent nodes */}
          <section className="panel">
            <h2 className="panel-title">
              <Icon name="node" size={16} /> {t('node.adjacentTitle')}
            </h2>
            {neighbors.length > 0 ? (
              <ul className="adjacent-list">
                {neighbors.map((neighbor) => (
                  <li key={neighbor.id}>
                    <Link to={`/node/${encodeURIComponent(neighbor.id)}`} className="adjacent-item">
                      <span className="adjacent-name">{nodeDisplayName(neighbor)}</span>
                      {neighbor.nodeType ? <span className="pill pill-type pill-sm">{neighbor.nodeType}</span> : null}
                    </Link>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="muted">{t('node.noAdjacent')}</p>
            )}
          </section>

          {/* Rights & authority */}
          {rights.length > 0 ? (
            <section className="panel">
              <h2 className="panel-title">{t('node.rightsTitle')}</h2>
              <dl className="kv-list">
                {rights.map((tag) => (
                  <div className="kv-row" key={tag.key}>
                    <dt>{tag.key}</dt>
                    <dd>{tag.value}</dd>
                  </div>
                ))}
              </dl>
            </section>
          ) : null}

          {/* Labels */}
          {labels.length > 0 ? (
            <section className="panel">
              <h2 className="panel-title">{t('node.labelsTitle')}</h2>
              <div className="pill-row">
                {labels.map((label) => (
                  <span key={label} className="pill pill-label">
                    {label}
                  </span>
                ))}
              </div>
            </section>
          ) : null}

          {/* Other tags */}
          {otherTags.length > 0 ? (
            <section className="panel">
              <h2 className="panel-title">{t('node.tagsTitle')}</h2>
              <dl className="kv-list">
                {otherTags.map(([key, value]) => (
                  <div className="kv-row" key={key}>
                    <dt>{key}</dt>
                    <dd>{String(value)}</dd>
                  </div>
                ))}
              </dl>
            </section>
          ) : null}
        </aside>
      </div>
    </div>
  );
}
