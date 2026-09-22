import 'package:flutter/material.dart';

import 'changes.dart';
import 'main.dart';
import 'widgets.dart';

/// What the last scan changed: new APIs (tap to open), changed facts, and the ones that went.
class ChangesPage extends StatelessWidget {
  final CatalogueDiff diff;
  const ChangesPage({super.key, required this.diff});

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final t = diff.at;
    Widget row(String key, String name, {String? detail, bool gone = false}) {
      final v = state.find(key);
      return ListTile(
        dense: true,
        enabled: v != null,
        leading: v == null ? const SizedBox(width: 32) : BrandTile(v, size: 32),
        title: Text(name, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontWeight: FontWeight.w700, color: gone ? scheme.onSurfaceVariant : null, decoration: gone ? TextDecoration.lineThrough : null)),
        subtitle: detail == null ? null : Text(detail, maxLines: 2, overflow: TextOverflow.ellipsis),
        onTap: v == null ? null : () => openDetail(context, v),
      );
    }

    return Scaffold(
      appBar: AppBar(
        toolbarHeight: 48,
        title: const Text('Since the last scan', style: TextStyle(fontSize: 17, fontWeight: FontWeight.w800)),
        actions: [
          if (!diff.seen)
            TextButton(
                onPressed: () {
                  state.markChangesSeen();
                  Navigator.of(context).pop();
                },
                child: const Text('Dismiss')),
        ],
      ),
      body: ListView(padding: EdgeInsets.only(bottom: 24 + MediaQuery.viewPaddingOf(context).bottom), children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
          child: Text('${diff.summary} · scanned ${t.day}/${t.month}/${t.year}', style: TextStyle(fontWeight: FontWeight.w700, color: scheme.onSurfaceVariant)),
        ),
        if (diff.added.isNotEmpty) ...[
          const Padding(padding: EdgeInsets.fromLTRB(16, 12, 16, 0), child: FieldLabel('NEW')),
          for (final (k, n) in diff.added) row(k, n),
        ],
        if (diff.changed.isNotEmpty) ...[
          const Padding(padding: EdgeInsets.fromLTRB(16, 12, 16, 0), child: FieldLabel('CHANGED')),
          for (final c in diff.changed) row(c.key, c.name, detail: c.what),
        ],
        if (diff.gone.isNotEmpty) ...[
          const Padding(padding: EdgeInsets.fromLTRB(16, 12, 16, 0), child: FieldLabel('NO LONGER LISTED')),
          for (final (k, n) in diff.gone) row(k, n, gone: true),
        ],
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 16, 16, 0),
          child: Text('An API that is no longer listed by any directory drops out of the catalogue; your favourites, tags, notes and keys for it stay.', style: TextStyle(color: scheme.onSurfaceVariant, fontSize: 12.5, height: 1.4)),
        ),
      ]),
    );
  }
}
