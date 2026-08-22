/**
 * Translation resources. English is the baseline catalog; add sibling locale
 * objects here and register them in localeRegistry.js to expand coverage.
 */

const en = {
  translation: {
    app: {
      name: 'Pneuma',
      tagline: 'Pneuma - information brought to life',
      explore: 'Explore'
    },
    nav: {
      search: 'Search',
      ask: 'Ask',
      backToSearch: 'Back to search'
    },
    common: {
      loading: 'Loading…',
      retry: 'Try again',
      error: 'Something went wrong',
      score: 'Score',
      openInNewTab: 'Opens in a new tab',
      copy: 'Copy',
      copied: 'Copied',
      close: 'Close',
      none: 'None'
    },
    theme: {
      toggleToDark: 'Switch to dark theme',
      toggleToLight: 'Switch to light theme'
    },
    notFound: {
      code: '404',
      title: 'Page not found',
      message: 'The page you’re looking for doesn’t exist or may have moved.',
      backToSearch: 'Back to search'
    },
    language: {
      label: 'Language'
    },
    login: {
      title: 'Explore the archive',
      subtitle: 'Search a subject’s official knowledge graph, follow the connections, and ask questions grounded in real sources.',
      serverUrl: 'Server URL',
      serverUrlPlaceholder: 'http://localhost:8080',
      email: 'Email',
      emailPlaceholder: 'you@example.com',
      password: 'Password',
      passwordPlaceholder: 'Your password',
      submit: 'Enter',
      submitting: 'Connecting…',
      hint: 'Default demo credentials: admin@pneuma / password',
      failed: 'Could not sign in. Check the server URL and your credentials.',
      showPassword: 'Show password',
      hidePassword: 'Hide password'
    },
    search: {
      heroTitle: 'What do you want to explore?',
      heroSubtitle: 'Search across the archive to find a representative set of moments, works, people, and places.',
      placeholder: 'Search a topic, work, person, or moment…',
      submit: 'Search',
      resultsFor: 'Results for “{{query}}”',
      resultCount_one: '{{count}} result',
      resultCount_other: '{{count}} results',
      emptyTitle: 'Start exploring',
      emptyBody: 'Try a name, a song, an event, or a theme. You’ll get a representative set of nodes to dive into.',
      noResultsTitle: 'No matches',
      noResultsBody: 'We couldn’t find anything for “{{query}}”. Try a different or broader term.',
      searching: 'Searching the archive…'
    },
    node: {
      loading: 'Loading node…',
      notFoundTitle: 'Node not found',
      notFoundBody: 'This node could not be loaded. It may have moved or the link may be incomplete.',
      contentTitle: 'Contents',
      noContent: 'This node has no text content.',
      rightsTitle: 'Rights & authority',
      linksTitle: 'Links',
      noLinks: 'No links found in this node.',
      adjacentTitle: 'Adjacent nodes',
      noAdjacent: 'No adjacent nodes.',
      relationshipsTitle: 'Relationships',
      noRelationships: 'No relationships recorded for this node.',
      labelsTitle: 'Labels',
      tagsTitle: 'Tags',
      copyId: 'Copy node ID',
      view: 'View'
    },
    ask: {
      heroTitle: 'Ask a question',
      heroSubtitle: 'Get an answer grounded in the archive, with the sources that support it.',
      scope: 'Scope',
      scopeHint: 'Limit answers to content ingested with these labels and tags.',
      scopeClear: 'Clear scope',
      scopeLabels: 'Labels',
      scopeTags: 'Tags',
      scopeLabelsHint: 'Only include content carrying every one of these labels.',
      scopeTagsHint: 'Only include content carrying every one of these tag key/value pairs.',
      scopeLabelPlaceholder: 'label',
      scopeTagKeyPlaceholder: 'key',
      scopeTagValuePlaceholder: 'value',
      placeholder: 'Ask anything about the archive…',
      submit: 'Ask',
      asking: 'Thinking…',
      answerTitle: 'Answer',
      sourcesTitle: 'Sources',
      noSources: 'No sources were cited for this answer.',
      groundedYes: 'Grounded in the archive',
      groundedNo: 'Not enough information in the archive',
      emptyTitle: 'Ask the archive',
      emptyBody: 'Questions are answered only from curated, official sources — so you can trust where the answer comes from.',
      answeredBy: 'Answered by {{model}}'
    }
  }
};

export const resources = { en };
export default resources;
