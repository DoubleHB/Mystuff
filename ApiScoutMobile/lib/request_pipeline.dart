// One place for what Try it, Compare's "Measure now" and the generators all rely on: variables are filled in, {key}
// is filled in only for the send, the key is put back to {key} in what comes back, and a request that was refused
// before anything was sent is not history.
import 'tester.dart';
import 'variables.dart';

/// Fills [typedUrl], [typedHeaders] and [typedBody] (variables first, then {key} with [key]) and sends. The saved key
/// never follows a redirect to another host, and is scrubbed from the summary and body that come back - whether it
/// stood as {key} or was typed out in full.
Future<TestResult> runRequest(String method, String typedUrl, String typedHeaders, String typedBody, {Map<String, String> vars = const {}, String key = ''}) async {
  // variables first, then the key: a variable's value is never searched for {key}
  var url = fillVariables(typedUrl, vars, escape: true), headers = fillVariables(typedHeaders, vars), body = fillVariables(typedBody, vars);
  final needsKey = usesKeyPlaceholder('$url$headers$body');
  if (needsKey && key.isEmpty) return const TestResult(false, false, 'The request uses {key} but no key is saved under Keys → My key. Save one first.', '');
  if (needsKey) {
    url = fillKey(url, key, escape: true);
    headers = fillKey(headers, key);
    body = fillKey(body, key);
  }
  final carriesKey = needsKey || (key.length >= 6 && '$url$headers$body'.contains(key));
  var r = await sendTest(method, url, headers, body, carriesKey: carriesKey);
  // some APIs echo the request back: what is shown (and can be copied) says {key} again
  if (carriesKey) r = TestResult(r.ok, r.isJson, scrubKey(r.summary, key), scrubKey(r.body, key));
  return r;
}

/// A result that was refused before anything was sent (a bad URL, a bad header line, no key) is not worth a history line.
bool wasSent(TestResult r) => !r.summary.startsWith('That is not') && !r.summary.startsWith('This header line') && !r.summary.startsWith('The request uses {key}');
