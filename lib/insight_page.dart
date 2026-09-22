import 'package:flutter/material.dart';

import 'insight.dart';
import 'knowledge.dart';
import 'main.dart';
import 'widgets.dart';

/// "More about this API": what the known facts mean for you, then how the provider describes it on its own page.
class InsightPage extends StatefulWidget {
  final ApiView view;
  const InsightPage({super.key, required this.view});
  @override
  State<InsightPage> createState() => _InsightPageState();
}

class _InsightPageState extends State<InsightPage> {
  ApiInfo? _info;
  bool _loading = true;

  ApiView get v => widget.view;

  @override
  void initState() {
    super.initState();
    _read();
  }

  Future<void> _read() async {
    setState(() => _loading = true);
    final info = await readInsight(v.entry);
    if (mounted) {
      setState(() {
        _info = info;
        _loading = false;
      });
    }
  }

  Widget _bullet(BuildContext context, String text, {IconData icon = Icons.check_circle_outline, Color? color}) => Padding(
        padding: const EdgeInsets.only(bottom: 8),
        child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Padding(padding: const EdgeInsets.only(top: 1, right: 10), child: Icon(icon, size: 18, color: color ?? Theme.of(context).colorScheme.primary)),
          Expanded(child: SelectableText(text, style: const TextStyle(height: 1.35))),
        ]),
      );

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final muted = TextStyle(color: scheme.onSurfaceVariant, fontSize: 12.5);
    final info = _info;
    return Scaffold(
      appBar: AppBar(
        title: Text('About ${v.name}', overflow: TextOverflow.ellipsis),
        actions: [
          IconButton(tooltip: 'Copy all of this as Markdown', icon: const Icon(Icons.copy), onPressed: () => copyText(context, insightMarkdown(v, info), 'Summary')),
          IconButton(tooltip: 'Open the docs', icon: const Icon(Icons.open_in_new), onPressed: () => openUrl(context, v.url)),
        ],
      ),
      body: ListView(padding: EdgeInsets.only(top: 8, bottom: 24 + MediaQuery.viewPaddingOf(context).bottom), children: [
        Section('At a glance', first: true, children: [
          if (v.entry.description.isNotEmpty) Padding(padding: const EdgeInsets.only(bottom: 10), child: Text(v.entry.description)),
          for (final b in benefits(v))
            b.startsWith('Careful') || b.startsWith('No HTTPS')
                ? _bullet(context, b, icon: Icons.warning_amber_outlined, color: badgeColor(context, 'Key needed'))
                : _bullet(context, b),
        ]),
        Section('In the provider\'s words', children: [
          if (_loading)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 12),
              child: Row(children: [SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2)), SizedBox(width: 12), Text('Reading the provider\'s page…')]),
            )
          else if (info != null) ...[
            if (info.title.isNotEmpty) Padding(padding: const EdgeInsets.only(bottom: 6), child: Text(info.title, style: const TextStyle(fontWeight: FontWeight.w600))),
            if (info.summary.isNotEmpty) SelectableText(info.summary, style: const TextStyle(height: 1.4)),
            if (info.error != null)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Text(info.error!, style: TextStyle(color: badgeColor(context, 'Key needed'))),
              ),
            const SizedBox(height: 8),
            Wrap(spacing: 8, children: [
              OutlinedButton.icon(onPressed: () => openUrl(context, v.url), icon: const Icon(Icons.open_in_new, size: 18), label: const Text('Open in browser')),
              if (info.error != null) TextButton(onPressed: _read, child: const Text('Try again')),
            ]),
          ],
        ]),
        if (info != null && info.features.isNotEmpty)
          Section('Features', children: [for (final f in info.features) _bullet(context, f, icon: Icons.arrow_right, color: scheme.onSurfaceVariant)]),
        if (info != null && info.sections.isNotEmpty)
          Section('The docs cover', children: [
            Wrap(spacing: 6, runSpacing: 6, children: [
              for (final s in info.sections)
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                  decoration: BoxDecoration(border: Border.all(color: scheme.outlineVariant), borderRadius: BorderRadius.circular(12)),
                  child: Text(s, style: const TextStyle(fontSize: 12.5)),
                ),
            ]),
          ]),
        if (info != null && !_loading)
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
            child: Text('Read from ${info.source}. Only what the page itself says is shown - nothing is made up.', style: muted),
          ),
      ]),
    );
  }
}
