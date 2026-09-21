import 'dart:async';

import 'package:flutter/material.dart';

import 'docs_scanner.dart';
import 'insight_page.dart';
import 'knowledge.dart';
import 'main.dart';
import 'tester.dart';
import 'user_data.dart';
import 'variables.dart';
import 'widgets.dart';

/// One API: what it is, what its key situation is, and a place to try a request.
class DetailPage extends StatefulWidget {
  final ApiView view;
  const DetailPage({super.key, required this.view});
  @override
  State<DetailPage> createState() => _DetailPageState();
}

class _DetailPageState extends State<DetailPage> {
  late final _url = TextEditingController(text: widget.view.example ?? widget.view.url);
  late final _headers = TextEditingController(text: _defaultHeader);
  final _body = TextEditingController();
  final _key = TextEditingController();
  late final _tags = TextEditingController(text: state.tagsOf(widget.view).join(', '));
  late final _note = TextEditingController(text: state.noteOf(widget.view));
  late final _vars = TextEditingController(text: variablesToText(state.variablesOf(widget.view)));
  final _tryItKey = GlobalKey();
  Timer? _noteTimer;
  String _method = 'GET';
  bool _sending = false;
  bool _showKey = false;
  bool _keyDirty = false;
  TestResult? _result;

  ApiView get v => widget.view;

  @override
  void initState() {
    super.initState();
    state.keyOf(v).then((k) {
      if (mounted && !_keyDirty) _key.text = k;
    });
    state.ensureHistory().then((_) {
      if (mounted) setState(() {});
    });
  }

  String get _requestText => '${_url.text}${_headers.text}${methodHasBody(_method) ? _body.text : ''}';

  /// Placeholders in the request that have no line in the Variables box yet.
  List<String> get _unlistedVariables {
    final have = parseVariables(_vars.text).keys.map((k) => k.toLowerCase()).toSet();
    return [for (final n in variableNames(_requestText)) if (!have.contains(n.toLowerCase())) n];
  }

  void _saveVariables() {
    final map = parseVariables(_vars.text);
    if (variablesToText(map) != variablesToText(state.variablesOf(v))) state.setVariables(v, map);
  }

  /// "Try" beside an endpoint the docs scan found: load it into Try it; a plain GET with nothing to fill in is sent straight away.
  void _tryEndpoint(FoundItem item) {
    setState(() {
      _method = testMethods.contains(item.method) ? item.method : 'GET';
      _url.text = item.value;
      _result = null;
    });
    final context = _tryItKey.currentContext;
    if (context != null) Scrollable.ensureVisible(context, duration: const Duration(milliseconds: 300), alignment: 0.05);
    if (_method == 'GET' && !item.value.contains('{')) _send();
  }

  void _restore(TestHistoryEntry h) => setState(() {
        _method = testMethods.contains(h.method) ? h.method : 'GET';
        _url.text = h.url;
        _headers.text = h.headers;
        _body.text = h.body;
        final (text, isJson) = formatBody(h.response);
        _result = TestResult(h.ok, isJson, '${h.summary}\n(from ${h.when} - press Test this API to send it again)', text);
      });

  Future<void> _showHistory() async {
    final picked = await showModalBottomSheet<Object>(
      context: context,
      showDragHandle: true,
      isScrollControlled: true,
      builder: (context) => SafeArea(
        child: ConstrainedBox(
          constraints: BoxConstraints(maxHeight: MediaQuery.sizeOf(context).height * 0.7),
          child: ListView(shrinkWrap: true, children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 0, 20, 8),
              child: Text('Earlier requests', style: Theme.of(context).textTheme.titleLarge),
            ),
            for (final h in state.historyOf(v))
              ListTile(
                leading: Icon(h.ok ? Icons.check_circle_outline : Icons.error_outline, color: badgeColor(context, h.ok ? 'Open' : 'Key needed')),
                title: Text('${h.when}  ·  ${h.method}  ·  ${h.firstLine}', maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 13.5)),
                subtitle: Text(h.url, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontFamily: 'monospace', fontSize: 11.5)),
                onTap: () => Navigator.of(context).pop(h),
              ),
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 4, 20, 12),
              child: Row(children: [
                Expanded(child: Text('The last $historyPerApi per API, kept encrypted on this phone. {key} is never stored.', style: Theme.of(context).textTheme.bodySmall)),
                TextButton(onPressed: () => Navigator.of(context).pop('clear'), child: const Text('Clear')),
              ]),
            ),
          ]),
        ),
      ),
    );
    if (!mounted) return;
    if (picked is TestHistoryEntry) _restore(picked);
    if (picked == 'clear') {
      await state.clearHistory(v);
      if (mounted) setState(() {});
    }
  }

  void _saveNoteSoon() {
    _noteTimer?.cancel();
    _noteTimer = Timer(const Duration(milliseconds: 700), () => state.setNote(v, _note.text));
  }

  void _saveTags() {
    final tags = parseTags(_tags.text);
    if (tags.join('|') != state.tagsOf(v).join('|')) state.setTags(v, tags);
    final tidy = tags.join(', ');
    if (_tags.text != tidy) _tags.text = tidy;
  }

  Future<void> _saveKey() async {
    FocusScope.of(context).unfocus();
    final had = state.hasKey(v);
    await state.setKey(v, _key.text);
    _keyDirty = false;
    if (!mounted) return;
    final saved = state.hasKey(v);
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(
          behavior: SnackBarBehavior.floating,
          content: Text(saved ? '✓ Key saved in the phone\'s secure storage - {key} in Try it stands for it' : had ? 'Key removed from this phone' : 'Type or paste a key first')));
    setState(() {});
  }

  Future<void> _addToCollection() async {
    final name = await showDialog<String>(context: context, builder: (_) => CollectionDialog(already: state.collectionsOf(v)));
    if (name == null || name.trim().isEmpty || !mounted) return;
    final used = state.addToCollection(v, name);
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(behavior: SnackBarBehavior.floating, content: Text('✓ Added to "$used" - it is in the category menu')));
  }

  /// "Header: X-Api-Key: abc" in the key knowledge means the key travels in a header.
  String get _defaultHeader => (v.hint?.keyUsage ?? '').startsWith('Header: ') ? v.hint!.keyUsage!.substring(8) : '';

  @override
  void dispose() {
    // what was typed last must not be lost to the back button
    // (a moment later: listeners must not be told while the page is being taken apart)
    final view = v, note = _note.text, tags = parseTags(_tags.text), vars = parseVariables(_vars.text);
    final noteWaiting = _noteTimer?.isActive ?? false;
    _noteTimer?.cancel();
    Future.microtask(() {
      if (noteWaiting) state.setNote(view, note);
      if (tags.join('|') != state.tagsOf(view).join('|')) state.setTags(view, tags);
      if (variablesToText(vars) != variablesToText(state.variablesOf(view))) state.setVariables(view, vars);
    });
    _url.dispose();
    _headers.dispose();
    _body.dispose();
    _key.dispose();
    _tags.dispose();
    _note.dispose();
    _vars.dispose();
    super.dispose();
  }

  Future<void> _send() async {
    FocusScope.of(context).unfocus();
    final body = methodHasBody(_method) ? _body.text : '';
    final method = _method, typedUrl = _url.text, typedHeaders = _headers.text;
    _saveVariables();
    final vars = parseVariables(_vars.text);
    final missing = missingVariables('$typedUrl$typedHeaders$body', vars);
    if (missing.isNotEmpty) {
      setState(() => _result = TestResult(false, false,
          'Not sent: ${missing.map((m) => '{$m}').join(', ')} ${missing.length == 1 ? 'has' : 'have'} no value. Give ${missing.length == 1 ? 'it' : 'them'} one in the Variables box (name = value).', ''));
      return;
    }
    // variables first, then the key: a variable's value is never searched for {key}
    var url = fillVariables(typedUrl, vars, escape: true), headers = fillVariables(typedHeaders, vars), sentBody = fillVariables(body, vars);
    final filledUrl = url, filledHeaders = headers, filledBody = sentBody;
    if (usesKeyPlaceholder('$url$headers$sentBody')) {
      // the key as typed wins over the saved one, so a key can be tried before it is saved
      final key = _key.text.trim().isNotEmpty ? _key.text.trim() : await state.keyOf(v);
      if (key.isEmpty) {
        setState(() => _result = const TestResult(false, false, 'The request uses {key} but no key is saved under \'My key\' above. Save one first.', ''));
        return;
      }
      url = fillKey(url, key, escape: true);
      headers = fillKey(headers, key);
      sentBody = fillKey(sentBody, key);
    }
    setState(() => _sending = true);
    var r = await sendTest(method, url, headers, sentBody);
    if (url != filledUrl || headers != filledHeaders || sentBody != filledBody) {
      // some APIs echo the request back: what is shown (and can be copied) says {key} again
      final key = _key.text.trim().isNotEmpty ? _key.text.trim() : await state.keyOf(v);
      String hide(String s) => key.length < 6 ? s : s.replaceAll(key, '{key}').replaceAll(Uri.encodeComponent(key), '{key}');
      r = TestResult(r.ok, r.isJson, hide(r.summary), hide(r.body));
    }
    // the history keeps the request as typed, so neither the key nor a variable's value is written with it
    // (a request that was refused before sending - a bad URL or header line - is not history)
    if (!r.summary.startsWith('That is not') && !r.summary.startsWith('This header line')) await state.addHistory(v, TestHistoryEntry(at: DateTime.now(), method: method, url: typedUrl, headers: typedHeaders, body: body, ok: r.ok, summary: r.summary, response: r.body));
    if (mounted) {
      setState(() {
        _result = r;
        _sending = false;
      });
    }
  }

  String _asText() => [
        v.name,
        v.entry.description,
        'Category: ${v.entry.category}',
        'Docs: ${v.url}',
        'Auth: ${v.authLabel}  ·  ${v.keyHeadline}',
        'Free access: ${v.accessLabel}',
        if (v.hasDemoKey) 'Demo key: ${v.demoKey}',
        if (v.hint?.keyUsage != null) 'How the key is sent: ${v.hint!.keyUsage}',
        if (v.example != null) 'Example: ${v.example}',
        if (v.hint?.signupUrl != null) 'Get a key: ${v.hint!.signupUrl}',
      ].join('\n');

  String _asMarkdown() => [
        '### [${v.name}](${v.url})',
        '',
        v.entry.description,
        '',
        '- **Category:** ${v.entry.category}',
        '- **Auth:** ${v.authLabel} - ${v.keyHeadline}',
        '- **Free access:** ${v.accessLabel}',
        if (v.hasDemoKey) '- **Demo key:** `${v.demoKey}`',
        if (v.example != null) '- **Example:** `${v.example}`',
        if (v.hint?.signupUrl != null) '- **Get a key:** ${v.hint!.signupUrl}',
      ].join('\n');

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final muted = TextStyle(color: scheme.onSurfaceVariant, fontSize: 12.5);
    final e = v.entry;
    return ListenableBuilder(
      listenable: state,
      builder: (context, _) => Scaffold(
        appBar: AppBar(
          title: Text(v.name, overflow: TextOverflow.ellipsis),
          actions: [
            IconButton(
              tooltip: state.isFavourite(v) ? 'Remove from favourites' : 'Add to favourites',
              icon: Icon(state.isFavourite(v) ? Icons.star : Icons.star_border, color: state.isFavourite(v) ? const Color(0xFFF5B83D) : null),
              onPressed: () => state.toggleFavourite(v),
            ),
            IconButton(tooltip: 'Open the docs', icon: const Icon(Icons.open_in_new), onPressed: () => openUrl(context, v.url)),
          ],
        ),
        // an explicit padding replaces the automatic one, so the gesture / button bar at the bottom is added back by hand
        body: ListView(padding: EdgeInsets.only(top: 8, bottom: 24 + MediaQuery.viewPaddingOf(context).bottom), children: [
          // ---- what it is
          Card(
            margin: const EdgeInsets.fromLTRB(12, 0, 12, 12),
            child: Padding(
              padding: const EdgeInsets.all(14),
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  BrandTile(v, size: 46),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Text(v.name, style: Theme.of(context).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.bold)),
                      Text(v.host, style: muted),
                    ]),
                  ),
                ]),
                const SizedBox(height: 8),
                Text('${e.category}  ·  found in ${e.sources.join(', ')}', style: muted),
                const SizedBox(height: 6),
                Text(e.description),
                const FieldLabel('DOCS'),
                ValueRow(v.url, onCopy: () => copyText(context, v.url, 'Docs URL'), onOpen: () => openUrl(context, v.url)),
                const SizedBox(height: 8),
                Wrap(spacing: 14, runSpacing: 4, children: [
                  Text('Auth: ${v.authLabel}', style: muted),
                  Text('HTTPS: ${e.https == null ? '?' : e.https! ? 'Yes' : 'No'}', style: muted),
                  Text('CORS: ${e.cors.isEmpty ? '?' : e.cors}', style: muted),
                  if (e.health != null) Text('Health: ${e.health}%', style: muted),
                ]),
                const SizedBox(height: 10),
                FilledButton.tonalIcon(
                  onPressed: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => InsightPage(view: v))),
                  icon: const Icon(Icons.info_outline, size: 18),
                  label: const Text('More about this API'),
                ),
              ]),
            ),
          ),

          // ---- keys & access
          Section('Keys & access', children: [
            Container(
              width: double.infinity,
              padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
              decoration: BoxDecoration(color: badgeColor(context, v.keyBadge).withValues(alpha: 0.16), borderRadius: BorderRadius.circular(8)),
              child: Text(v.keyHeadline, style: TextStyle(fontWeight: FontWeight.w600, color: badgeColor(context, v.keyBadge))),
            ),
            const FieldLabel('WHAT IS FREE'),
            Text.rich(TextSpan(children: [
              TextSpan(text: v.accessLabel, style: TextStyle(fontWeight: FontWeight.w600, color: badgeColor(context, v.accessLabel))),
              TextSpan(text: '  -  ${v.accessNote}'),
            ])),
            if (v.hasDemoKey) ...[
              const FieldLabel('DEMO KEY  (published by the provider - shared and rate limited)'),
              ValueRow(v.demoKey!, strong: true, onCopy: () => copyText(context, v.demoKey!, 'Demo key')),
            ],
            if (v.hint?.keyUsage != null) ...[
              const FieldLabel('HOW THE KEY IS SENT'),
              ValueRow(v.hint!.keyUsage!, onCopy: () => copyText(context, v.hint!.keyUsage!, 'Key usage')),
            ],
            if (v.example != null) ...[
              const FieldLabel('EXAMPLE REQUEST  (works as is)'),
              ValueRow(v.example!, onCopy: () => copyText(context, v.example!, 'Example request'), onOpen: () => openUrl(context, v.example)),
            ],
            const FieldLabel('HOW TO GET A KEY'),
            Text(v.howTo, style: const TextStyle(height: 1.4)),
            if (v.hint?.signupUrl != null) ...[
              const FieldLabel('GET YOUR OWN KEY HERE'),
              ValueRow(v.hint!.signupUrl!, onCopy: () => copyText(context, v.hint!.signupUrl!, 'Sign-up link'), onOpen: () => openUrl(context, v.hint!.signupUrl)),
            ],
            const SizedBox(height: 8),
            Wrap(spacing: 8, runSpacing: 4, children: [
              OutlinedButton(onPressed: () => copyText(context, v.howTo, 'How-to'), child: const Text('Copy these instructions')),
              FilledButton.tonalIcon(
                onPressed: state.isScanningDocs(v) ? null : () => state.scanDocsFor(v),
                icon: state.isScanningDocs(v)
                    ? const SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2))
                    : const Icon(Icons.travel_explore, size: 18),
                label: Text(state.isScanningDocs(v) ? 'Reading the docs…' : state.docsScanOf(v) == null ? 'Scan docs for key info' : 'Scan the docs again'),
              ),
            ]),
            const Divider(height: 28),
            FieldLabel(state.hasKey(v) ? 'MY KEY  (saved on this phone)' : 'MY KEY'),
            TextField(
              controller: _key,
              obscureText: !_showKey,
              autocorrect: false,
              enableSuggestions: false,
              keyboardType: TextInputType.visiblePassword,
              style: const TextStyle(fontFamily: 'monospace', fontSize: 13),
              onChanged: (_) => _keyDirty = true,
              decoration: InputDecoration(
                hintText: 'Paste the key you were given',
                border: const OutlineInputBorder(),
                isDense: true,
                suffixIcon: IconButton(
                  tooltip: _showKey ? 'Hide the key' : 'Show the key',
                  icon: Icon(_showKey ? Icons.visibility_off : Icons.visibility),
                  onPressed: () => setState(() => _showKey = !_showKey),
                ),
              ),
            ),
            const SizedBox(height: 8),
            Wrap(spacing: 8, runSpacing: 4, children: [
              FilledButton.tonal(onPressed: _saveKey, child: const Text('Save key')),
              if (state.hasKey(v)) ...[
                OutlinedButton(
                    onPressed: () async {
                      final k = await state.keyOf(v);
                      if (context.mounted) copyText(context, k, 'Your key');
                    },
                    child: const Text('Copy')),
                TextButton(
                    onPressed: () {
                      _key.clear();
                      _saveKey();
                    },
                    child: const Text('Remove')),
              ],
            ]),
            const SizedBox(height: 6),
            Text(
              state.vaultProblem ??
                  'Kept encrypted by the Android keystore, on this phone only. Write {key} in a Try it URL, header or body and it is filled in when the request is sent - never in what you copy.',
              style: muted,
            ),
          ]),

          // ---- what a docs scan found
          if (state.docsScanOf(v) case final scan?)
            Section('Live look at the docs', children: [
              Text('${scan.summary}  ·  read ${scan.scannedAt.day}/${scan.scannedAt.month}/${scan.scannedAt.year}', style: muted),
              if (scan.error != null) Padding(padding: const EdgeInsets.only(top: 6), child: Text(scan.error!, style: TextStyle(color: badgeColor(context, 'Key needed')))),
              for (final (i, item) in scan.items.indexed) ...[
                if (i == 0 || scan.items[i - 1].kind != item.kind) FieldLabel(item.kind.toUpperCase()) else const SizedBox(height: 6),
                if (item.kind == 'Free tier / limits' || item.kind == 'Pricing' || item.kind == 'Note')
                  SelectableText('• ${item.value}', style: const TextStyle(height: 1.35))
                else
                  ValueRow(item.isEndpoint ? '${item.method} ${item.value}' : item.value,
                      onCopy: () => copyText(context, item.value, item.kind), onOpen: item.isLink && !item.isEndpoint ? () => openUrl(context, item.value) : null),
                if (item.note.isNotEmpty && !item.isEndpoint) Padding(padding: const EdgeInsets.only(top: 2), child: Text(item.note, style: muted)),
                if (item.isEndpoint)
                  Row(children: [
                    Expanded(child: Text(item.note, style: muted)),
                    TextButton.icon(onPressed: () => _tryEndpoint(item), icon: const Icon(Icons.play_arrow, size: 18), label: const Text('Try')),
                  ]),
                if (item.kind == 'Sample key')
                  Align(
                    alignment: Alignment.centerLeft,
                    child: TextButton(
                      onPressed: () {
                        setState(() {
                          _key.text = item.value;
                          _keyDirty = true;
                        });
                        ScaffoldMessenger.of(context)
                          ..hideCurrentSnackBar()
                          ..showSnackBar(const SnackBar(behavior: SnackBarBehavior.floating, content: Text('Put into My key (not saved yet) - {key} in Try it now stands for it')));
                      },
                      child: const Text('Use as my key'),
                    ),
                  ),
              ],
              const SizedBox(height: 8),
              OutlinedButton.icon(onPressed: () => openUrl(context, scan.pageUrl), icon: const Icon(Icons.open_in_new, size: 18), label: const Text('Open in browser')),
              const SizedBox(height: 4),
              Text('Only what the provider prints on its own public pages. A sample key may be a working demo key or just an example.', style: muted),
            ]),

          // ---- try it
          Section('Try it', key: _tryItKey, children: [
            Text(
              v.example != null
                  ? 'Pre-filled with a request that works as it is - press Test this API.'
                  : 'ApiScout only knows this API\'s docs page. Paste an endpoint URL from the docs, pick the method, then press Test this API.',
              style: muted,
            ),
            const SizedBox(height: 10),
            Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
              DropdownButton<String>(
                value: _method,
                items: [for (final m in testMethods) DropdownMenuItem(value: m, child: Text(m, style: const TextStyle(fontWeight: FontWeight.w600)))],
                onChanged: (m) => setState(() => _method = m ?? 'GET'),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: TextField(
                  controller: _url,
                  onChanged: (_) => setState(() {}), // a new {placeholder} offers a Variables line
                  minLines: 1,
                  maxLines: 4,
                  keyboardType: TextInputType.url,
                  autocorrect: false,
                  style: const TextStyle(fontFamily: 'monospace', fontSize: 12.5),
                  decoration: const InputDecoration(labelText: 'Request URL', border: OutlineInputBorder(), isDense: true),
                ),
              ),
            ]),
            const SizedBox(height: 10),
            TextField(
              controller: _headers,
              onChanged: (_) => setState(() {}),
              minLines: 1,
              maxLines: 4,
              autocorrect: false,
              style: const TextStyle(fontFamily: 'monospace', fontSize: 12.5),
              decoration: const InputDecoration(labelText: 'Headers (optional, one Name: value per line)', border: OutlineInputBorder(), isDense: true),
            ),
            if (methodHasBody(_method)) ...[
              const SizedBox(height: 10),
              TextField(
                controller: _body,
                minLines: 3,
                maxLines: 10,
                autocorrect: false,
                style: const TextStyle(fontFamily: 'monospace', fontSize: 12.5),
                decoration: const InputDecoration(labelText: 'Body (JSON, form a=b&c=d, XML or text)', border: OutlineInputBorder(), isDense: true),
              ),
            ],
            const SizedBox(height: 10),
            TextField(
              controller: _vars,
              minLines: 1,
              maxLines: 6,
              autocorrect: false,
              style: const TextStyle(fontFamily: 'monospace', fontSize: 12.5),
              onChanged: (_) => setState(() {}),
              onTapOutside: (_) {
                FocusScope.of(context).unfocus();
                _saveVariables();
              },
              decoration: const InputDecoration(
                labelText: 'Variables (optional, one name = value per line)',
                helperText: '{name} in the URL, headers or body is replaced. Always there: {today} {yesterday} {tomorrow} {now} {timestamp}',
                helperMaxLines: 3,
                border: OutlineInputBorder(),
                isDense: true,
              ),
            ),
            if (_unlistedVariables.isNotEmpty)
              Wrap(spacing: 6, children: [
                for (final name in _unlistedVariables)
                  ActionChip(
                    label: Text('+ $name ='),
                    visualDensity: VisualDensity.compact,
                    tooltip: 'Add a line for {$name}',
                    onPressed: () => setState(() => _vars.text = '${_vars.text.trimRight()}${_vars.text.trim().isEmpty ? '' : '\n'}$name = '),
                  ),
              ]),
            const SizedBox(height: 10),
            Wrap(spacing: 8, runSpacing: 4, crossAxisAlignment: WrapCrossAlignment.center, children: [
              FilledButton.icon(
                onPressed: _sending ? null : _send,
                icon: _sending ? const SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2)) : const Icon(Icons.play_arrow),
                label: Text(_sending ? 'Sending…' : 'Test this API'),
              ),
              OutlinedButton(
                  onPressed: () {
                    // variables are filled in, {key} is not: a copied command never carries the saved key
                    final vars = parseVariables(_vars.text);
                    copyText(context, toCurl(_method, fillVariables(_url.text, vars, escape: true), fillVariables(_headers.text, vars), fillVariables(_body.text, vars)), 'cURL command');
                  },
                  child: const Text('Copy as cURL')),
              if (state.historyOf(v).isNotEmpty)
                OutlinedButton.icon(onPressed: _showHistory, icon: const Icon(Icons.history, size: 18), label: Text('History (${state.historyOf(v).length})')),
            ]),
            if (_result != null) ...[
              const SizedBox(height: 12),
              Text(_result!.summary,
                  style: TextStyle(fontWeight: FontWeight.w600, fontSize: 12.5, color: badgeColor(context, _result!.ok ? 'Open' : 'Key needed'))),
              if (_result!.body.isNotEmpty) ...[
                Row(children: [
                  const Expanded(child: FieldLabel('RESPONSE')),
                  TextButton.icon(onPressed: () => copyText(context, _result!.body, 'Response'), icon: const Icon(Icons.copy, size: 16), label: const Text('Copy')),
                ]),
                Container(
                  constraints: const BoxConstraints(maxHeight: 360),
                  width: double.infinity,
                  padding: const EdgeInsets.all(10),
                  decoration: BoxDecoration(color: scheme.surfaceContainerHighest, borderRadius: BorderRadius.circular(8)),
                  child: SingleChildScrollView(
                    child: SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: SelectableText(_result!.body, style: const TextStyle(fontFamily: 'monospace', fontSize: 12)),
                    ),
                  ),
                ),
              ],
            ],
          ]),

          // ---- mine
          Section('My tags, notes & collections', children: [
            TextField(
              controller: _tags,
              autocorrect: false,
              textInputAction: TextInputAction.done,
              onSubmitted: (_) => _saveTags(),
              onTapOutside: (_) {
                FocusScope.of(context).unfocus();
                _saveTags();
              },
              decoration: const InputDecoration(labelText: 'Tags (comma separated, up to 8)', hintText: 'weather, side project', border: OutlineInputBorder(), isDense: true),
            ),
            if (state.user.tagCounts.any((t) => !parseTags(_tags.text).any((mine) => mine.toLowerCase() == t.$1.toLowerCase()))) ...[
              const SizedBox(height: 6),
              Wrap(spacing: 6, children: [
                for (final (tag, _) in state.user.tagCounts.take(12))
                  if (!parseTags(_tags.text).any((mine) => mine.toLowerCase() == tag.toLowerCase()))
                    ActionChip(
                      label: Text('+ $tag'),
                      visualDensity: VisualDensity.compact,
                      onPressed: () {
                        _tags.text = [...parseTags(_tags.text), tag].join(', ');
                        _saveTags();
                      },
                    ),
              ]),
            ],
            const SizedBox(height: 12),
            TextField(
              controller: _note,
              minLines: 2,
              maxLines: 8,
              textCapitalization: TextCapitalization.sentences,
              onChanged: (_) => _saveNoteSoon(),
              decoration: const InputDecoration(labelText: 'Notes (saved as you type)', border: OutlineInputBorder(), isDense: true, alignLabelWithHint: true),
            ),
            const SizedBox(height: 10),
            Wrap(spacing: 6, runSpacing: 4, crossAxisAlignment: WrapCrossAlignment.center, children: [
              for (final c in state.collectionsOf(v))
                InputChip(label: Text('📁 $c'), visualDensity: VisualDensity.compact, onDeleted: () => state.removeFromCollection(v, c), deleteButtonTooltipMessage: 'Take out of "$c"'),
              OutlinedButton.icon(onPressed: _addToCollection, icon: const Icon(Icons.create_new_folder_outlined, size: 18), label: const Text('Add to a collection…')),
            ]),
          ]),

          // ---- copy as
          Section('Copy this API as…', children: [
            Wrap(spacing: 8, runSpacing: 4, children: [
              OutlinedButton(onPressed: () => copyText(context, _asText(), 'Details'), child: const Text('Details')),
              OutlinedButton(onPressed: () => copyText(context, _asMarkdown(), 'Markdown'), child: const Text('Markdown')),
              OutlinedButton(
                  onPressed: () => copyText(context, toCurl('GET', v.example ?? v.url, _defaultHeader, ''), 'cURL command'), child: const Text('cURL')),
            ]),
          ]),
        ]),
      ),
    );
  }
}

/// Pick one of the user's collections, or name a new one. Pops the name.
class CollectionDialog extends StatefulWidget {
  final List<String> already;
  const CollectionDialog({super.key, required this.already});
  @override
  State<CollectionDialog> createState() => _CollectionDialogState();
}

class _CollectionDialogState extends State<CollectionDialog> {
  final _name = TextEditingController();

  @override
  void dispose() {
    _name.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final others = [for (final c in state.user.collections.keys) if (!widget.already.contains(c)) c];
    return AlertDialog(
      title: const Text('Add to a collection'),
      content: SingleChildScrollView(
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          if (others.isNotEmpty) ...[
            Wrap(spacing: 6, runSpacing: 4, children: [for (final c in others) ActionChip(label: Text(c), onPressed: () => Navigator.of(context).pop(c))]),
            const SizedBox(height: 14),
          ],
          TextField(
            controller: _name,
            autofocus: others.isEmpty,
            maxLength: 40,
            textCapitalization: TextCapitalization.sentences,
            onSubmitted: (t) => Navigator.of(context).pop(t),
            decoration: InputDecoration(labelText: others.isEmpty ? 'Name of the collection' : 'Or a new collection', border: const OutlineInputBorder(), isDense: true),
          ),
        ]),
      ),
      actions: [
        TextButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Cancel')),
        FilledButton(onPressed: () => Navigator.of(context).pop(_name.text), child: const Text('Add')),
      ],
    );
  }
}
