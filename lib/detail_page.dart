import 'package:flutter/material.dart';

import 'knowledge.dart';
import 'main.dart';
import 'tester.dart';
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
  String _method = 'GET';
  bool _sending = false;
  TestResult? _result;

  ApiView get v => widget.view;

  /// "Header: X-Api-Key: abc" in the key knowledge means the key travels in a header.
  String get _defaultHeader => (v.hint?.keyUsage ?? '').startsWith('Header: ') ? v.hint!.keyUsage!.substring(8) : '';

  @override
  void dispose() {
    _url.dispose();
    _headers.dispose();
    _body.dispose();
    super.dispose();
  }

  Future<void> _send() async {
    FocusScope.of(context).unfocus();
    setState(() => _sending = true);
    final r = await sendTest(_method, _url.text, _headers.text, methodHasBody(_method) ? _body.text : '');
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
        body: ListView(padding: const EdgeInsets.only(top: 8, bottom: 24), children: [
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
            OutlinedButton(onPressed: () => copyText(context, v.howTo, 'How-to'), child: const Text('Copy these instructions')),
          ]),

          // ---- try it
          Section('Try it', children: [
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
            Wrap(spacing: 8, runSpacing: 4, crossAxisAlignment: WrapCrossAlignment.center, children: [
              FilledButton.icon(
                onPressed: _sending ? null : _send,
                icon: _sending ? const SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2)) : const Icon(Icons.play_arrow),
                label: Text(_sending ? 'Sending…' : 'Test this API'),
              ),
              OutlinedButton(onPressed: () => copyText(context, toCurl(_method, _url.text, _headers.text, _body.text), 'cURL command'), child: const Text('Copy as cURL')),
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
