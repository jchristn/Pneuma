import { describe, it, expect } from 'vitest';
import { ontologyError, exportText, newRule, RULE_FIELDS, RULE_TYPES } from './ontologyUtils';

describe('ontologyUtils', () => {
  it('appends the server-listed problems to the error message', () => {
    const err = { message: 'Approval refused', parsed: { problems: ['Rule r1 names undeclared type X.', 'Concept c has no label.'] } };
    expect(ontologyError(err, 'fallback')).toBe('Approval refused\n• Rule r1 names undeclared type X.\n• Concept c has no label.');
  });

  it('falls back when the error carries nothing', () => {
    expect(ontologyError(null, 'fallback')).toBe('fallback');
  });

  it('pretty-prints JSON exports and leaves other formats alone', () => {
    expect(exportText('json', '{"a":1}')).toBe('{\n  "a": 1\n}');
    expect(exportText('turtle', '@prefix x: <urn:x:> .')).toBe('@prefix x: <urn:x:> .');
    expect(exportText('jsonld', 'not json')).toBe('not json');
  });

  it('describes the fields of every rule type', () => {
    RULE_TYPES.forEach((type) => expect(RULE_FIELDS[type].length).toBeGreaterThan(0));
    expect(newRule().id).toBe('');
    expect(newRule('MaxOutgoing').ruleType).toBe('MaxOutgoing');
  });
});
