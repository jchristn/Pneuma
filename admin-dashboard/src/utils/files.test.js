import { describe, expect, it } from 'vitest';
import { acceptAttribute, fileExtension, formatFileSize, isInlineLink, isTextContentType, linkDisplayName, linkFileName, linkKind, precheckFile } from './files';

const settings = {
  maxUploadBytes: 1000,
  maxFileNameLength: 20,
  supportedTypes: [
    { documentType: 'Pdf', extensions: ['pdf'], mimeTypes: ['application/pdf'] },
    { documentType: 'Markdown', extensions: ['md', 'markdown'], mimeTypes: ['text/markdown'] }
  ]
};

describe('link display', () => {
  it('names an uploaded link by its file name', () => {
    const link = { id: 'lnk_1', sourceKind: 'Inline', url: 'pneuma-inline://lnk_1/Q3%20report.pdf', fileName: 'Q3 report.pdf', title: 'Q3' };
    expect(isInlineLink(link)).toBe(true);
    expect(linkFileName(link)).toBe('Q3 report.pdf');
    expect(linkKind(link)).toBe('File');
    expect(linkDisplayName(link)).toBe('Q3 report.pdf');
  });

  it('reads the file name from the inline URI when the field is missing', () => {
    expect(linkDisplayName({ url: 'pneuma-inline://lnk_2/r%C3%A9sum%C3%A9.docx' })).toBe('résumé.docx');
  });

  it('names pushed text by its title and URL links by their URL', () => {
    expect(linkDisplayName({ id: 'lnk_3', sourceKind: 'Inline', url: 'pneuma-inline://lnk_3', title: 'Notes' })).toBe('Notes');
    expect(linkKind({ sourceKind: 'Inline', url: 'pneuma-inline://lnk_3' })).toBe('Text');
    expect(linkDisplayName({ sourceKind: 'Url', url: 'https://example.com/a', title: 'A' })).toBe('https://example.com/a');
    expect(linkKind({ sourceKind: 'Crawl', url: 's3://b/k' })).toBe('Crawl');
    expect(linkDisplayName(null)).toBe('');
  });
});

describe('precheckFile', () => {
  it('accepts a supported file within the limits', () => {
    expect(precheckFile({ name: 'a.pdf', size: 10, type: 'application/pdf' }, settings)).toBeNull();
    expect(precheckFile({ name: 'noext', size: 10, type: 'text/markdown' }, settings)).toBeNull();
  });

  it('refuses empty, oversized, long-named, and unsupported files', () => {
    expect(precheckFile({ name: 'a.pdf', size: 0 }, settings).code).toBe('empty');
    expect(precheckFile({ name: 'a.pdf', size: 2000 }, settings).code).toBe('tooLarge');
    expect(precheckFile({ name: 'a-very-long-file-name.pdf', size: 10 }, settings).code).toBe('nameTooLong');
    expect(precheckFile({ name: 'a.exe', size: 10, type: 'application/x-msdownload' }, settings)).toEqual({ code: 'unsupported', params: { ext: 'exe' } });
  });

  it('accepts anything non-empty when no settings are known', () => {
    expect(precheckFile({ name: 'a.exe', size: 10 }, null)).toBeNull();
  });
});

describe('helpers', () => {
  it('formats sizes and extensions', () => {
    expect(formatFileSize(512)).toBe('512 B');
    expect(formatFileSize(1536)).toBe('1.5 KB');
    expect(formatFileSize(5 * 1024 * 1024)).toBe('5.0 MB');
    expect(fileExtension('Report.PDF')).toBe('pdf');
    expect(fileExtension('.bashrc')).toBe('');
  });

  it('builds the accept attribute and classifies content types', () => {
    expect(acceptAttribute(settings)).toBe('.pdf,application/pdf,.md,.markdown,text/markdown');
    expect(acceptAttribute(null)).toBe('');
    expect(isTextContentType('application/json; charset=utf-8')).toBe(true);
    expect(isTextContentType('application/pdf')).toBe(false);
  });
});
