import { useState, useEffect, useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import ResourceView from '../components/ResourceView';
import CopyableId from '../components/CopyableId';
import StatusPill, { toneForStatus } from '../components/StatusPill';
import IngestionLogModal from '../components/IngestionLogModal';
import BulkAddLinksModal from '../components/BulkAddLinksModal';
import Modal from '../components/Modal';
import JsonViewer from '../components/JsonViewer';
import { formatDateTime } from '../i18n/formatters';

// Build { value, label } option lists from an ingestion-endpoint entry.
function endpointOptions(list) {
  return (Array.isArray(list) ? list : []).map((e) => ({
    value: e.id ?? e.Id,
    label: e.name || e.model || e.id || e.Id
  }));
}

// Build { value, label } option lists from a vector-collection entry (name + dimensionality).
function collectionSelectOptions(list) {
  return (Array.isArray(list) ? list : []).map((c) => {
    const dims = c.dimensionality ?? c.Dimensionality;
    const name = c.name || c.Name || c.id || c.Id;
    return { value: c.id ?? c.Id, label: dims ? `${name} (${dims}d)` : name };
  });
}

function LinksView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const [logLink, setLogLink] = useState(null);
  const [subjects, setSubjects] = useState([]);
  const [selectedSubjectId, setSelectedSubjectId] = useState(searchParams.get('subjectId') || '');
  const [embeddingOptions, setEmbeddingOptions] = useState([]);
  const [completionOptions, setCompletionOptions] = useState([]);
  const [collectionOptions, setCollectionOptions] = useState([]);
  const [showBulk, setShowBulk] = useState(false);
  const [bulkCreated, setBulkCreated] = useState(null);
  const [refreshKey, setRefreshKey] = useState(0);
  // Pipeline-artifact modals: { title, data } for the JSON viewer, a string
  // notice for the "not available yet" message, and a flag while a fetch runs.
  const [artifact, setArtifact] = useState(null);
  const [artifactNotice, setArtifactNotice] = useState('');
  const [artifactLoading, setArtifactLoading] = useState(false);

  // Load the subject list once for the filter dropdown and the create form.
  useEffect(() => {
    let cancelled = false;
    apiClient.list('subjects', { maxResults: 1000 })
      .then((resp) => { if (!cancelled) setSubjects(normalizeList(resp).items); })
      .catch(() => { if (!cancelled) setSubjects([]); });
    return () => { cancelled = true; };
  }, [apiClient]);

  // Load available embedding/completion model endpoints for the create + bulk forms.
  useEffect(() => {
    let cancelled = false;
    apiClient.listIngestionEndpoints()
      .then((resp) => {
        if (cancelled) return;
        setEmbeddingOptions(endpointOptions(resp?.embedding || resp?.Embedding));
        setCompletionOptions(endpointOptions(resp?.completion || resp?.Completion));
      })
      .catch(() => {
        if (cancelled) return;
        setEmbeddingOptions([]);
        setCompletionOptions([]);
      });
    return () => { cancelled = true; };
  }, [apiClient]);

  // Load the vector collections operators have defined, for the create + bulk forms.
  useEffect(() => {
    let cancelled = false;
    apiClient.listCollections()
      .then((resp) => { if (!cancelled) setCollectionOptions(collectionSelectOptions(normalizeList(resp).items)); })
      .catch(() => { if (!cancelled) setCollectionOptions([]); });
    return () => { cancelled = true; };
  }, [apiClient]);

  // Keep local selection in sync when arriving via a subjectId query param.
  useEffect(() => {
    setSelectedSubjectId(searchParams.get('subjectId') || '');
  }, [searchParams]);

  const subjectName = useCallback((id) => {
    const match = subjects.find((c) => c.id === id);
    return match ? (match.displayName || match.name || id) : id;
  }, [subjects]);

  const columns = [
    { key: 'url', label: 'URL', cellClass: 'wrap', render: (r) => (
      <a href={r.url} target="_blank" rel="noopener noreferrer">{r.url}</a>
    ) },
    { key: 'title', label: 'Title', render: (r) => r.title || '—' },
    { key: 'status', label: 'Status', render: (r) => <StatusPill label={r.status} tone={toneForStatus(r.status)} /> },
    { key: 'subjectId', label: 'Subject', render: (r) => (
      <span title={r.subjectId}>{subjectName(r.subjectId) || <CopyableId value={r.subjectId} truncateLen={12} />}</span>
    ) },
    { key: 'lastIngestedUtc', label: 'Last Ingested', render: (r) => formatDateTime(r.lastIngestedUtc) },
    { key: 'lastError', label: 'Last Error', cellClass: 'wrap', sortable: false, render: (r) => r.lastError || '—' }
  ];

  const subjectOptions = subjects.map((c) => ({ value: c.id, label: c.displayName || c.name || c.id }));

  const noEndpoints = embeddingOptions.length === 0 || completionOptions.length === 0 || collectionOptions.length === 0;
  const noCollections = collectionOptions.length === 0;

  const formFields = [
    { name: 'subjectId', label: t('links.subject'), type: 'select', required: true, placeholder: t('links.selectSubject'), default: selectedSubjectId || '', options: subjectOptions, tip: 'Which subject this source belongs to. Its extracted content and answers are scoped to that subject.' },
    { name: 'url', label: 'URL', required: true, placeholder: 'https://...', tip: 'The web page or document URL to ingest. Pneuma crawls it, extracts entities and text, embeds it, and indexes it.' },
    { name: 'title', label: t('links.title'), tip: 'Optional friendly name for this source. Defaults to the page title when left blank.' },
    { name: 'embeddingEndpointId', label: t('links.embeddingModel'), type: 'select', required: true, placeholder: t('links.selectModel'), options: embeddingOptions, default: embeddingOptions.length === 1 ? embeddingOptions[0].value : undefined, tip: 'The embedding endpoint used to vectorize this source. Must produce vectors matching the target collection’s dimensionality.' },
    { name: 'completionEndpointId', label: t('links.completionModel'), type: 'select', required: true, placeholder: t('links.selectModel'), options: completionOptions, default: completionOptions.length === 1 ? completionOptions[0].value : undefined, tip: 'The completion endpoint used during ingestion to extract the knowledge-graph (entities and relationships) from this source.' },
    { name: 'collectionId', label: t('links.collection'), type: 'select', required: true, placeholder: t('links.selectCollection'), options: collectionOptions, default: collectionOptions.length === 1 ? collectionOptions[0].value : undefined, tip: 'The RecallDB collection this source’s chunks are indexed into. Choose one whose dimensionality matches the embedding model.' }
  ];

  // Links are created via /v1.0/subjects/{subjectId}/links which enqueues ingestion.
  const subject = (client, body) =>
    client.create(`subjects/${encodeURIComponent(body.subjectId)}/links`, {
      url: body.url,
      title: body.title,
      embeddingEndpointId: body.embeddingEndpointId,
      completionEndpointId: body.completionEndpointId,
      collectionId: body.collectionId
    });

  // Server-side filtering via the subject's links endpoint when a subject is chosen.
  const fetcher = useCallback((client) => (
    selectedSubjectId
      ? client.list(`subjects/${encodeURIComponent(selectedSubjectId)}/links`)
      : client.list('links')
  ), [selectedSubjectId]);

  const onFilterChange = (value) => {
    setSelectedSubjectId(value);
    const next = new URLSearchParams(searchParams);
    if (value) next.set('subjectId', value);
    else next.delete('subjectId');
    setSearchParams(next, { replace: true });
  };

  // Map an ApiError to the friendly not-available notice on 404, else its message.
  const artifactErrorMessage = useCallback((err) => (
    err?.status === 404 ? t('links.artifactUnavailable') : (err?.message || t('links.artifactError'))
  ), [t]);

  // Fetch a JSON pipeline artifact (atoms/chunks/vectors/subgraph) and show it
  // in the shared JsonViewer, or surface a friendly notice on failure.
  const openArtifact = useCallback(async (item, kind, title) => {
    setArtifactLoading(true);
    setArtifactNotice('');
    try {
      const data = await apiClient.getLinkArtifact(item.id, kind);
      setArtifact({ title, data });
    } catch (err) {
      setArtifactNotice(artifactErrorMessage(err));
    } finally {
      setArtifactLoading(false);
    }
  }, [apiClient, artifactErrorMessage]);

  // Fetch the raw source document and show it as text in the in-app viewer (with a copy-to-clipboard
  // icon) rather than triggering a browser download/save for non-renderable content types.
  const openSource = useCallback(async (item) => {
    setArtifactLoading(true);
    setArtifactNotice('');
    try {
      const { blob } = await apiClient.getLinkSource(item.id);
      const text = await blob.text();
      setArtifact({ title: t('links.viewSource'), data: text, copyLabel: t('links.viewSource'), size: 'full' });
    } catch (err) {
      setArtifactNotice(artifactErrorMessage(err));
    } finally {
      setArtifactLoading(false);
    }
  }, [apiClient, artifactErrorMessage, t]);

  const toolbar = (
    <div className="filter-bar">
      <div className="field">
        <label htmlFor="links-subject-filter">{t('links.filterBySubject')}</label>
        <select id="links-subject-filter" value={selectedSubjectId} onChange={(e) => onFilterChange(e.target.value)}>
          <option value="">{t('links.allSubjects')}</option>
          {subjectOptions.map((o) => (
            <option key={o.value} value={o.value}>{o.label}</option>
          ))}
        </select>
      </div>
    </div>
  );

  return (
    <>
      <ResourceView
        key={refreshKey}
        resourceKey="links"
        singular="link"
        title={t('nav.links')}
        subtitle="Content links and their ingestion status"
        columns={columns}
        formFields={formFields}
        subject={subject}
        fetcher={fetcher}
        toolbar={toolbar}
        headerActions={(
          <button type="button" className="button-secondary" onClick={() => setShowBulk(true)}>
            {t('links.addMultiple')}
          </button>
        )}
        createDisabled={noEndpoints}
        createNotice={noEndpoints ? (noCollections ? t('links.noCollections') : t('links.noEndpoints')) : null}
        capabilities={{ create: true, edit: false, delete: true, viewJson: true }}
        idField="id"
        extraActions={[
          { key: 'ingestionLog', label: t('links.viewIngestionLog'), onClick: (item) => setLogLink(item) },
          { key: 'viewSource', label: t('links.viewSource'), onClick: (item) => openSource(item) },
          { key: 'viewAtoms', label: t('links.viewAtoms'), onClick: (item) => openArtifact(item, 'atoms', t('links.artifactAtoms')) },
          { key: 'viewChunks', label: t('links.viewChunks'), onClick: (item) => openArtifact(item, 'chunks', t('links.artifactChunks')) },
          { key: 'viewVectors', label: t('links.viewVectors'), onClick: (item) => openArtifact(item, 'vectors', t('links.artifactVectors')) },
          { key: 'viewSubgraph', label: t('links.viewSubgraph'), onClick: (item) => openArtifact(item, 'subgraph', t('links.artifactSubgraph')) }
        ]}
      />
      {logLink && <IngestionLogModal link={logLink} onClose={() => setLogLink(null)} />}
      {artifactLoading && (
        <Modal title={t('common.loading')} size="sm" onClose={() => setArtifactLoading(false)}>
          <div className="table-loading"><div className="loading-spinner" /></div>
        </Modal>
      )}
      {artifact && (
        <JsonViewer title={artifact.title} data={artifact.data} copyLabel={artifact.copyLabel} size={artifact.size} onClose={() => setArtifact(null)} />
      )}
      {artifactNotice && (
        <Modal
          title={t('common.notice')}
          size="sm"
          onClose={() => setArtifactNotice('')}
          footer={<button type="button" className="button-primary" onClick={() => setArtifactNotice('')}>{t('common.close')}</button>}
        >
          <p className="confirm-text">{artifactNotice}</p>
        </Modal>
      )}
      {showBulk && (
        <BulkAddLinksModal
          subjectOptions={subjectOptions}
          embeddingOptions={embeddingOptions}
          completionOptions={completionOptions}
          collectionOptions={collectionOptions}
          onClose={() => setShowBulk(false)}
          onCreated={(created) => {
            setShowBulk(false);
            setBulkCreated(created);
            setRefreshKey((k) => k + 1);
          }}
        />
      )}
      {bulkCreated !== null && (
        <Modal
          title={t('links.addMultiple')}
          size="sm"
          onClose={() => setBulkCreated(null)}
          footer={<button type="button" className="button-primary" onClick={() => setBulkCreated(null)}>{t('common.close')}</button>}
        >
          <p className="confirm-text">{t('links.bulkCreated', { count: bulkCreated })}</p>
        </Modal>
      )}
    </>
  );
}

export default LinksView;
