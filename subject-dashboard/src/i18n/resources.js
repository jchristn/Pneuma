/**
 * Translation resources. English is the complete baseline catalog; Spanish
 * carries a representative subset (shell + common actions) to exercise the
 * runtime. Missing keys fall back to English.
 */

const en = {
  translation: {
    app: {
      name: 'Pneuma',
      tagline: 'Subject Dashboard',
      loading: 'Loading...'
    },
    notFound: {
      code: '404',
      title: 'Page not found',
      message: 'The page you’re looking for doesn’t exist or may have moved.',
      back: 'Back to dashboard'
    },
    nav: {
      overview: 'Overview',
      subjects: 'My Subjects',
      links: 'Links',
      ingestion: 'Ingestion',
      ask: 'Ask',
      observability: 'Observability',
      requestHistory: 'Request History',
      apiExplorer: 'API Explorer',
      settings: 'Settings',
      content: 'Content'
    },
    ask: {
      subtitle: 'Ask a grounded question of your archive; the answer streams as it is generated.',
      placeholder: 'Ask a question…',
      submit: 'Ask',
      streaming: 'Streaming…',
      answerTitle: 'Answer',
      groundedYes: 'Grounded',
      groundedNo: 'Ungrounded',
      sourcesTitle: 'Sources',
      answeredBy: 'Answered by {{model}}'
    },
    login: {
      subtitle: 'Manage your content and knowledge graph',
      serverUrl: 'Server URL',
      email: 'Email',
      password: 'Password',
      connect: 'Sign In',
      connecting: 'Signing in...',
      showPassword: 'Show password',
      hidePassword: 'Hide password',
      error: 'Failed to sign in. Check your server URL and credentials.'
    },
    topbar: {
      github: 'View on GitHub',
      theme: 'Toggle theme',
      logout: 'Logout',
      admin: 'Admin',
      tenantAdmin: 'Tenant Admin',
      subject: 'Subject'
    },
    common: {
      refresh: 'Refresh',
      cancel: 'Cancel',
      save: 'Save',
      create: 'Create',
      edit: 'Edit',
      delete: 'Delete',
      view: 'View',
      close: 'Close',
      confirm: 'Confirm',
      submit: 'Submit',
      retry: 'Retry',
      restart: 'Restart',
      copy: 'Copy',
      copied: 'Copied',
      loading: 'Loading...',
      none: 'None',
      never: 'Never',
      all: 'All',
      search: 'Search',
      actions: 'Actions',
      status: 'Status',
      required: 'Required'
    },
    home: {
      title: 'Overview',
      subtitle: 'A snapshot of your subjects, content links, and ingestion health.',
      kpiSubjects: 'My Subjects',
      kpiLinks: 'Links Submitted',
      kpiProcessing: 'Jobs Processing',
      kpiFailed: 'Jobs Failed',
      activity: 'API Activity',
      recentFailures: 'Recent Failures',
      noFailures: 'No failed ingestions. Everything looks healthy.',
      submitLinkCta: 'Submit a Link',
      viewIngestionCta: 'View Ingestion'
    },
    subjects: {
      title: 'My Subjects',
      subtitle: 'Subject profiles you manage. Content links are attached to a subject.',
      add: '+ Add Subject',
      displayName: 'Display Name',
      type: 'Type',
      description: 'Description',
      graphRoot: 'Graph Root',
      createTitle: 'Create Subject',
      editTitle: 'Edit Subject',
      empty: 'You have no subjects yet. Create one to start submitting content links.'
    },
    links: {
      title: 'Content Links',
      subtitle: 'Submit links to your content and track when each was last ingested.',
      submit: '+ Submit Link',
      url: 'URL',
      linkTitle: 'Title',
      subject: 'Subject',
      lastIngested: 'Last Ingested',
      lastError: 'Last Error',
      submitTitle: 'Submit Content Link',
      selectSubject: 'Select a subject',
      autoRefresh: 'Auto-refresh',
      empty: 'No content links yet. Submit one to enqueue ingestion.',
      submitHint: 'Submitting a link enqueues an ingestion job automatically.',
      viewIngestionLog: 'View Ingestion Log',
      embeddingModel: 'Embedding Model',
      completionModel: 'Completion Model',
      selectModel: 'Select a model',
      noEndpoints: 'No ingestion models are configured. Contact an administrator before submitting links.',
      addMultiple: '+ Add Multiple',
      addMultipleTitle: 'Add Multiple Links',
      urls: 'URLs',
      urlsHint: 'Enter one URL per line. Blank lines are ignored.',
      urlsRequired: 'Enter at least one URL.',
      selectModelsRequired: 'Select both an embedding and a completion model.',
      bulkCreated: '{{count}} link(s) submitted.'
    },
    ingestionLog: {
      title: 'Ingestion Log',
      run: 'Run',
      created: 'Created',
      completed: 'Completed',
      duration: 'Duration',
      emptyQueued: 'No steps recorded yet — ingestion is queued.',
      emptyNoRuns: 'No ingestion runs found for this link.'
    },
    ingestion: {
      title: 'Ingestion',
      subtitle: 'Track ingestion jobs and drill into per-stage progress and failures.',
      job: 'Job',
      link: 'Link',
      created: 'Created',
      updated: 'Updated',
      stages: 'Stages',
      timeline: 'Stage Timeline',
      restart: 'Restart Job',
      empty: 'No ingestion jobs found.',
      filterStatus: 'Filter by status',
      stage: 'Stage',
      message: 'Message',
      duration: 'Duration'
    },
    requests: {
      title: 'Request History',
      subtitle: 'Inspect captured API traffic and drill into request/response detail.',
      total: 'Total',
      successRate: 'Success Rate',
      failed: 'Failed',
      avgDuration: 'Avg Duration',
      when: 'When',
      method: 'Method',
      path: 'Path',
      duration: 'Duration',
      detail: 'Request Detail',
      empty: 'No requests match the current filters.',
      resetFilters: 'Reset Filters'
    },
    explorer: {
      title: 'API Explorer',
      subtitle: 'Execute live API calls against the server using your session token.',
      operations: 'Operations',
      execute: 'Execute',
      executing: 'Executing...',
      pathParams: 'Path Parameters',
      queryParams: 'Query Parameters',
      headers: 'Headers',
      body: 'Request Body',
      response: 'Response',
      resolvedUrl: 'Resolved URL',
      snippets: 'Code Snippets',
      noSpec: 'OpenAPI document (/openapi.json) is unavailable on this server.',
      confirmTitle: 'Confirm Destructive Operation',
      confirmMessage: 'This is a destructive operation. Are you sure you want to execute it?'
    },
    settings: {
      title: 'Settings',
      subtitle: 'Server connection, version, and your authentication context.',
      endpoint: 'API Endpoint',
      version: 'Server Version',
      service: 'Service',
      health: 'Health',
      authContext: 'Authentication Context',
      principal: 'Principal',
      email: 'Email',
      tenant: 'Tenant',
      userId: 'User ID',
      role: 'Role',
      theme: 'Theme',
      language: 'Language'
    },
    status: {
      submitted: 'Submitted',
      processing: 'Processing',
      ingested: 'Ingested',
      failed: 'Failed',
      queued: 'Queued',
      completed: 'Completed',
      running: 'Running',
      pending: 'Pending',
      unknown: 'Unknown'
    }
  }
};

const es = {
  translation: {
    app: { tagline: 'Panel del Subjecta', loading: 'Cargando...' },
    nav: {
      overview: 'Resumen',
      subjects: 'Mis Creadores',
      ask: 'Preguntar',
      links: 'Enlaces',
      ingestion: 'Ingesta',
      observability: 'Observabilidad',
      requestHistory: 'Historial de Solicitudes',
      apiExplorer: 'Explorador de API',
      settings: 'Ajustes',
      content: 'Contenido'
    },
    ask: {
      subtitle: 'Haz una pregunta fundamentada sobre tu archivo; la respuesta se transmite mientras se genera.',
      placeholder: 'Haz una pregunta…',
      submit: 'Preguntar',
      streaming: 'Transmitiendo…',
      answerTitle: 'Respuesta',
      groundedYes: 'Fundamentada',
      groundedNo: 'Sin fundamento',
      sourcesTitle: 'Fuentes',
      answeredBy: 'Respondido por {{model}}'
    },
    login: {
      subtitle: 'Gestiona tu contenido y grafo de conocimiento',
      serverUrl: 'URL del Servidor',
      email: 'Correo',
      password: 'Contrasena',
      connect: 'Iniciar Sesion',
      connecting: 'Conectando...'
    },
    common: {
      refresh: 'Actualizar',
      cancel: 'Cancelar',
      save: 'Guardar',
      create: 'Crear',
      edit: 'Editar',
      delete: 'Eliminar',
      view: 'Ver',
      close: 'Cerrar',
      submit: 'Enviar',
      restart: 'Reiniciar',
      actions: 'Acciones',
      status: 'Estado'
    }
  }
};

export const resources = { en, es };
export default resources;
