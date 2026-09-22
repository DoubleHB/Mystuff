import 'dart:convert';
import 'dart:math';

import 'package:flutter/material.dart';

import 'json_tree.dart';
import 'main.dart';
import 'widgets.dart';

/// A response on its own page: search with next/previous, and JSON as a tree whose nodes fold.
class ResponsePage extends StatefulWidget {
  final String title;
  final String body;
  final bool isJson;
  const ResponsePage({super.key, required this.title, required this.body, required this.isJson});

  @override
  State<ResponsePage> createState() => _ResponsePageState();
}

class _ResponsePageState extends State<ResponsePage> {
  final _query = TextEditingController();
  final _list = ScrollController();
  late final List<String> _lines = widget.body.split('\n');
  late final int _longest = _lines.fold(0, (m, l) => max(m, l.length));
  List<JsonLine>? _json;
  final _collapsed = <int>{};
  bool _tree = false;
  List<int> _matches = const [];
  int _pos = 0;

  @override
  void initState() {
    super.initState();
    if (widget.isJson) {
      try {
        _json = flattenJson(jsonDecode(widget.body));
        _tree = true;
      } catch (_) {
        // cut-off JSON: raw text only
      }
    }
  }

  @override
  void dispose() {
    _query.dispose();
    _list.dispose();
    super.dispose();
  }

  String get _q => _query.text.trim().toLowerCase();

  void _search() {
    _matches = matchingLines(_lines, _q);
    _pos = 0;
    setState(() {});
    if (!_tree && _matches.isNotEmpty) _showMatch();
  }

  void _step(int by) {
    if (_matches.isEmpty) return;
    setState(() => _pos = (_pos + by) % _matches.length);
    _showMatch();
  }

  void _showMatch() {
    if (!_list.hasClients) return;
    final target = (_matches[_pos] * _lineHeight - 120).clamp(0.0, _list.position.maxScrollExtent);
    _list.jumpTo(target);
  }

  double get _fontSize => MediaQuery.textScalerOf(context).scale(12);
  double get _lineHeight => _fontSize * 1.45 + 2;

  /// The text with the query lit; [strong] for the line the arrows are on.
  InlineSpan _lit(String text, {TextStyle? style, bool strong = false}) {
    final q = _q;
    if (q.isEmpty || !text.toLowerCase().contains(q)) return TextSpan(text: text, style: style);
    final lower = text.toLowerCase();
    final spans = <InlineSpan>[];
    var i = 0;
    while (true) {
      final at = lower.indexOf(q, i);
      if (at < 0) {
        spans.add(TextSpan(text: text.substring(i)));
        break;
      }
      if (at > i) spans.add(TextSpan(text: text.substring(i, at)));
      spans.add(TextSpan(text: text.substring(at, at + q.length), style: TextStyle(backgroundColor: strong ? const Color(0xFFF5B83D) : const Color(0x66F5B83D), color: strong ? const Color(0xFF111418) : null)));
      i = at + q.length;
    }
    return TextSpan(children: spans, style: style);
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      appBar: AppBar(
        toolbarHeight: 48,
        title: Text(widget.title, style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w800)),
        actions: [
          if (_json != null)
            IconButton(
              tooltip: _tree ? 'Show as text' : 'Show as a tree',
              icon: Icon(_tree ? Icons.notes : Icons.account_tree_outlined),
              onPressed: () => setState(() => _tree = !_tree),
            ),
          IconButton(tooltip: 'Copy the response', icon: const Icon(Icons.copy), onPressed: () => copyText(context, widget.body, 'Response')),
        ],
      ),
      body: Column(children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 4, 8, 4),
          child: Row(children: [
            Expanded(
              child: TextField(
                controller: _query,
                onChanged: (_) => _search(),
                onSubmitted: (_) => _step(1),
                textInputAction: TextInputAction.search,
                decoration: InputDecoration(
                  hintText: _tree ? 'Find a key or value' : 'Find in the response',
                  prefixIcon: const Icon(Icons.search, size: 20),
                  suffixIcon: _q.isEmpty
                      ? null
                      : IconButton(
                          icon: const Icon(Icons.close, size: 18),
                          onPressed: () {
                            _query.clear();
                            _search();
                          }),
                ),
              ),
            ),
            if (_q.isNotEmpty && !_tree) ...[
              const SizedBox(width: 6),
              Text(_matches.isEmpty ? '0' : '${_pos + 1}/${_matches.length}', style: TextStyle(fontSize: 12.5, fontWeight: FontWeight.w700, color: scheme.onSurfaceVariant, fontFeatures: const [FontFeature.tabularFigures()])),
              IconButton(tooltip: 'Previous match', visualDensity: VisualDensity.compact, icon: const Icon(Icons.keyboard_arrow_up), onPressed: _matches.isEmpty ? null : () => _step(-1)),
              IconButton(tooltip: 'Next match', visualDensity: VisualDensity.compact, icon: const Icon(Icons.keyboard_arrow_down), onPressed: _matches.isEmpty ? null : () => _step(1)),
            ],
          ]),
        ),
        Divider(height: 1, color: scheme.outlineVariant),
        Expanded(child: _tree ? _treeView(context) : _rawView(context)),
      ]),
    );
  }

  // ---- raw text: fixed-height lines, so a match number is a scroll position
  Widget _rawView(BuildContext context) {
    final width = max(MediaQuery.sizeOf(context).width, _longest * _fontSize * 0.62 + 32);
    final current = _matches.isEmpty ? -1 : _matches[_pos];
    return Scrollbar(
      controller: _list,
      child: SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        child: SizedBox(
          width: width,
          child: ListView.builder(
            controller: _list,
            itemExtent: _lineHeight,
            itemCount: _lines.length,
            padding: EdgeInsets.only(bottom: 24 + MediaQuery.viewPaddingOf(context).bottom),
            itemBuilder: (context, i) => Container(
              padding: const EdgeInsets.symmetric(horizontal: 16),
              alignment: Alignment.centerLeft,
              color: i == current ? Theme.of(context).colorScheme.surfaceContainerHighest : null,
              child: Text.rich(_lit(_lines[i], strong: i == current), maxLines: 1, softWrap: false, style: mono.copyWith(fontSize: 12)),
            ),
          ),
        ),
      ),
    );
  }

  // ---- tree: one row per visible node; tap a container to fold or unfold it, long-press a value to copy it
  Widget _treeView(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final rows = visibleJsonLines(_json!, _collapsed, _q);
    if (rows.isEmpty) return Center(child: Text('Nothing here matches', style: TextStyle(color: scheme.onSurfaceVariant)));
    Color valueColor(Object? v) => v is String ? scheme.primary : v == null ? scheme.onSurfaceVariant : scheme.onSurface;
    return ListView.builder(
      controller: _list,
      itemCount: rows.length,
      padding: EdgeInsets.only(top: 4, bottom: 24 + MediaQuery.viewPaddingOf(context).bottom),
      itemBuilder: (context, i) {
        final l = rows[i];
        final folded = l.container && _collapsed.contains(l.index) && _q.isEmpty;
        return InkWell(
          onTap: l.container && _q.isEmpty
              ? () => setState(() {
                    if (!_collapsed.remove(l.index)) _collapsed.add(l.index);
                  })
              : null,
          onLongPress: l.container ? null : () => copyText(context, l.value is String ? l.value as String : jsonValueText(l.value), 'Value'),
          child: Padding(
            padding: EdgeInsets.fromLTRB(8.0 + l.depth * 14, 5, 16, 5),
            child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
              SizedBox(
                width: 18,
                child: l.container ? Icon(folded ? Icons.chevron_right : Icons.expand_more, size: 16, color: scheme.onSurfaceVariant) : null,
              ),
              Expanded(
                child: Text.rich(
                  TextSpan(children: [
                    if (l.key != null) ...[
                      _lit(l.key!, style: TextStyle(fontWeight: FontWeight.w700, color: l.inList ? scheme.onSurfaceVariant : scheme.onSurface)),
                      TextSpan(text: ':  ', style: TextStyle(color: scheme.onSurfaceVariant)),
                    ],
                    l.container
                        ? TextSpan(text: '${l.valueText}${folded ? '  ${l.count} ${l.count == 1 ? 'item' : 'items'}' : ''}', style: TextStyle(color: scheme.onSurfaceVariant))
                        : _lit(l.valueText, style: TextStyle(color: valueColor(l.value))),
                  ]),
                  maxLines: 4,
                  overflow: TextOverflow.ellipsis,
                  style: mono.copyWith(fontSize: 12, height: 1.4),
                ),
              ),
            ]),
          ),
        );
      },
    );
  }
}
