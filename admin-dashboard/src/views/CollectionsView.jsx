import { useTranslation } from 'react-i18next';
import ResourceView from '../components/ResourceView';
import CopyableId from '../components/CopyableId';
import StatusPill from '../components/StatusPill';

// Vector collections live in RecallDB; Pneuma proxies their administration. Dimensionality is fixed at
// creation (it must match the embedding model used to ingest), so collections are create/delete only.
function CollectionsView() {
  const { t } = useTranslation();

  const columns = [
    { key: 'name', label: t('collections.name'), render: (r) => r.name || '—' },
    { key: 'description', label: t('collections.description'), cellClass: 'wrap', sortable: false, render: (r) => r.description || '—' },
    { key: 'dimensionality', label: t('collections.dimensionality'), render: (r) => r.dimensionality ?? r.Dimensionality ?? '—' },
    { key: 'active', label: t('collections.active'), render: (r) => (
      <StatusPill label={(r.active ?? r.Active) ? t('collections.activeYes') : t('collections.activeNo')} tone={(r.active ?? r.Active) ? 'success' : 'muted'} />
    ) },
    { key: 'id', label: 'ID', render: (r) => <CopyableId value={r.id ?? r.Id} truncateLen={12} /> }
  ];

  const formFields = [
    { name: 'name', label: t('collections.name'), required: true, placeholder: t('collections.namePlaceholder'), tip: 'A name for this vector collection. Group related sources into the same collection so they’re searched together.' },
    { name: 'description', label: t('collections.description'), tip: 'Optional notes about what this collection holds, shown in the list.' },
    { name: 'dimensionality', label: t('collections.dimensionality'), type: 'number', required: true, default: 768, help: t('collections.dimensionalityHelp'), tip: 'The embedding vector length, fixed at creation. It MUST match your embedding model’s output (e.g. 768 for nomic-embed-text). Wrong values make indexing fail.' }
  ];

  // Collections are created via PUT /v1.0/collections; coerce the dimensionality to a number.
  const create = (client, body) => client.createCollection({
    name: body.name,
    description: body.description || null,
    dimensionality: Number(body.dimensionality) || 768
  });

  return (
    <ResourceView
      resourceKey="collections"
      singular="collection"
      title={t('collections.title')}
      subtitle={t('collections.subtitle')}
      columns={columns}
      formFields={formFields}
      subject={create}
      idField="id"
      capabilities={{ create: true, edit: false, delete: true, viewJson: false }}
      duplicable
      duplicateTransform={(r) => ({ ...r, name: r.name ? `${r.name} (copy)` : '' })}
    />
  );
}

export default CollectionsView;
