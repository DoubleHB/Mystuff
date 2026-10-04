// 2-4 APIs side by side (the desktop app's Compare window): what each needs, what is free, and - on request -
// how each answers right now. This file is the facts and the Markdown table; compare_page.dart draws them.

import 'knowledge.dart';

const compareMin = 2, compareMax = 4;
const docsNowLabel = 'Docs site right now', exampleNowLabel = 'Live example call';
const notMeasured = 'not measured', noExample = 'no known example request';

typedef CompareFact = (String label, String Function(ApiView) value);

/// The rows of the comparison, in the desktop's order. What the phone cannot know ("first seen") is left out;
/// the two live rows read from [docsNow] and [exampleNow], which Measure now fills in.
List<CompareFact> compareFacts({
  required List<String> Function(ApiView) tagsOf,
  required String Function(ApiView) noteOf,
  required String Function(ApiView) docsNow,
  required String Function(ApiView) exampleNow,
}) =>
    [
      ('Category', (a) => a.entry.category),
      ('To start calling it', (a) => a.keyHeadline),
      ('What is free', (a) => '${a.accessLabel} - ${a.accessNote}'),
      ('Demo key', (a) => a.demoKey ?? '-'),
      ('How the key is sent', (a) => a.hint?.keyUsage ?? '-'),
      ('Get a key', (a) => a.hint?.signupUrl ?? '-'),
      ('How to get a key', (a) => a.howTo),
      ('HTTPS / CORS', (a) => '${a.entry.https == null ? '?' : a.entry.https! ? 'Yes' : 'No'} / ${a.entry.cors.isEmpty ? '?' : a.entry.cors}'),
      ('Health (freepublicapis)', (a) => a.entry.health == null ? '-' : '${a.entry.health}%'),
      (docsNowLabel, docsNow),
      (exampleNowLabel, exampleNow),
      ('Example request', (a) => a.example ?? '-'),
      ('Docs', (a) => a.url),
      ('Found in', (a) => a.entry.sources.join(', ')),
      ('My tags', (a) => tagsOf(a).isEmpty ? '-' : tagsOf(a).join(', ')),
      ('My notes', (a) => noteOf(a).trim().isEmpty ? '-' : noteOf(a).trim()),
    ];

/// The comparison as a Markdown table, as the desktop writes it: a column per API, a row per fact.
String compareMarkdown(List<ApiView> apis, Iterable<CompareFact> facts) {
  String cell(String s) => s.replaceAll('|', '\\|').replaceAll('\r', '').replaceAll('\n', '<br>');
  final sb = StringBuffer('| |${[for (final a in apis) ' [${cell(a.name)}](${a.url}) |'].join()}\n|---|${[for (final _ in apis) '---|'].join()}\n');
  for (final (label, value) in facts) {
    sb.write('| **$label** |${[for (final a in apis) ' ${cell(value(a))} |'].join()}\n');
  }
  return sb.toString();
}

/// The pick list after a tap on [key]: taken out when it was in, added when there is room. Null = full (nothing changes).
List<String>? togglePick(List<String> picked, String key) {
  if (picked.contains(key)) return [for (final k in picked) if (k != key) k];
  if (picked.length >= compareMax) return null;
  return [...picked, key];
}
