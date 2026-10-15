import 'dart:math';

import 'package:flutter/material.dart';
import 'compare.dart';
import 'detail_page.dart';
import 'knowledge.dart';
import 'main.dart';
import 'request_pipeline.dart';
import 'tester.dart';
import 'widgets.dart';

/// 2-4 APIs side by side, a column each: what each needs, what is free, and - on request - how each answers now.
class ComparePage extends StatefulWidget {
  final List<ApiView> apis;
  const ComparePage({super.key, required this.apis});
  @override
  State<ComparePage> createState() => _ComparePageState();
}

const _measuringText = 'measuring…';
const _monoFacts = {'Demo key', 'How the key is sent', 'Get a key', 'Example request', 'Docs'};

class _ComparePageState extends State<ComparePage> {
  late final Map<String, String> _docs = {for (final a in widget.apis) a.entry.key: notMeasured};
  late final Map<String, String> _example = {for (final a in widget.apis) a.entry.key: a.example == null ? noExample : notMeasured};
  bool _measuring = false;
  bool _measured = false;

  List<CompareFact> get _facts => compareFacts(
        tagsOf: state.tagsOf,
        noteOf: state.noteOf,
        docsNow: (a) => _docs[a.entry.key]!,
        exampleNow: (a) => _example[a.entry.key]!,
      );

  Future<void> _measure() async {
    setState(() {
      _measuring = true;
      for (final a in widget.apis) {
        _docs[a.entry.key] = _measuringText;
        if (a.example != null) _example[a.entry.key] = _measuringText;
      }
    });
    // each column stands alone: one API failing must not leave the others unmeasured
    await Future.wait([for (final a in widget.apis) _measureOne(a)]);
    if (mounted) {
      setState(() {
        _measuring = false;
        _measured = true;
      });
    }
  }

  Future<void> _measureOne(ApiView a) async {
    final key = a.entry.key;
    final docs = await runRequest('GET', a.url, '', '');
    if (!mounted) return;
    setState(() => _docs[key] = docs.summary.split('\n').first);
    final example = a.example;
    if (example == null) return;
    if (usesKeyPlaceholder('$example${a.exampleHeader}')) {
      // a request that needs the user's own key is theirs to send, from Try it
      setState(() => _example[key] = 'needs your key - send it from Try it');
      return;
    }
    final call = await runRequest('GET', example, a.exampleHeader, '');
    if (!mounted) return;
    setState(() => _example[key] = call.summary.split('\n').first);
  }

  String get _markdown => compareMarkdown(widget.apis, _facts);

  Future<void> _share() => shareText(context, _markdown, 'Compare: ${widget.apis.map((a) => a.name).join(' vs ')}');

  Color? _liveColor(BuildContext context, String label, String value) {
    if (label != docsNowLabel && label != exampleNowLabel) return null;
    if (value == notMeasured || value == noExample || value == _measuringText || value.startsWith('needs your key')) return Theme.of(context).colorScheme.onSurfaceVariant;
    return badgeColor(context, value.startsWith('2') ? 'Open' : 'Key needed');
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final apis = widget.apis;
    final facts = _facts;
    return Scaffold(
      appBar: AppBar(
        toolbarHeight: 48,
        title: Text('Compare ${apis.length} APIs'),
        actions: [
          IconButton(tooltip: 'Copy as Markdown', icon: const Icon(Icons.copy), onPressed: () => copyText(context, _markdown, 'Comparison')),
          IconButton(tooltip: 'Share as Markdown', icon: const Icon(Icons.share_outlined), onPressed: _share),
        ],
      ),
      body: Column(children: [
        Expanded(
          child: LayoutBuilder(builder: (context, constraints) {
            const gap = 14.0;
            // two columns share the screen; three or four keep a readable width and the table scrolls sideways
            final colW = max(168.0, (constraints.maxWidth - 32 - gap * (apis.length - 1)) / apis.length);
            final tableW = colW * apis.length + gap * (apis.length - 1);
            Widget row(List<Widget> cells) => Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  for (final (i, c) in cells.indexed) ...[if (i > 0) const SizedBox(width: gap), SizedBox(width: colW, child: c)],
                ]);
            return SingleChildScrollView(
              padding: const EdgeInsets.only(top: 4, bottom: 16),
              child: SingleChildScrollView(
                scrollDirection: Axis.horizontal,
                padding: const EdgeInsets.symmetric(horizontal: 16),
                child: SizedBox(
                  width: tableW,
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    row([for (final a in apis) _head(context, a)]),
                    for (final (label, value) in facts) ...[
                      Divider(height: 20, color: scheme.outlineVariant),
                      Padding(
                        padding: const EdgeInsets.only(bottom: 4),
                        child: Text(label.toUpperCase(), style: TextStyle(fontSize: 10.5, fontWeight: FontWeight.w700, letterSpacing: 1.1, color: scheme.primary)),
                      ),
                      row([
                        for (final a in apis)
                          _Cell(value(a), mono: _monoFacts.contains(label), color: _liveColor(context, label, value(a)), onCopy: () => copyText(context, value(a), label)),
                      ]),
                    ],
                  ]),
                ),
              ),
            );
          }),
        ),
        Divider(height: 1, color: scheme.outlineVariant),
        Padding(
          padding: EdgeInsets.fromLTRB(16, 10, 16, 10 + MediaQuery.viewPaddingOf(context).bottom),
          child: Row(children: [
            FilledButton.icon(
              onPressed: _measuring ? null : _measure,
              icon: _measuring ? SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2.5, color: scheme.onSurface)) : const Icon(Icons.timer_outlined, size: 18),
              label: Text(_measuring
                  ? 'Measuring…'
                  : _measured
                      ? 'Measure again'
                      : 'Measure now'),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Text('Checks each docs site and sends each known example request once. Long-press a value to copy it.',
                  style: TextStyle(fontSize: 11.5, height: 1.35, color: scheme.onSurfaceVariant)),
            ),
          ]),
        ),
      ]),
    );
  }

  /// A column's head: the brand tile, the name and the host; a tap opens the API.
  Widget _head(BuildContext context, ApiView a) => InkWell(
        onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => DetailPage(view: a))),
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 6),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            // no hero: the same API sits in the list underneath
            BrandTile(a, size: 36),
            const SizedBox(height: 8),
            Text(a.name, maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w800, letterSpacing: -0.3, height: 1.15)),
            const SizedBox(height: 2),
            Text(a.host.isEmpty ? a.url : a.host, maxLines: 1, overflow: TextOverflow.ellipsis, style: mono.copyWith(fontSize: 11.5, color: Theme.of(context).colorScheme.onSurfaceVariant)),
          ]),
        ),
      );
}

/// One value of the table. Long ones (the how-to) are cut to a screenful; a long-press copies the whole value.
class _Cell extends StatelessWidget {
  final String text;
  final bool mono;
  final Color? color;
  final VoidCallback onCopy;
  const _Cell(this.text, {this.mono = false, this.color, required this.onCopy});

  @override
  Widget build(BuildContext context) => InkWell(
        onLongPress: text == '-' ? null : onCopy,
        child: Text(
          text,
          maxLines: 14,
          overflow: TextOverflow.ellipsis,
          style: mono
              ? TextStyle(fontFamily: monoFamily, fontSize: 12, height: 1.4, color: color)
              : TextStyle(fontSize: 12.5, height: 1.4, color: color, fontWeight: color == null ? null : FontWeight.w700),
        ),
      );
}
