import 'package:flutter/material.dart';

import 'knowledge.dart';

/// JetBrains Mono for everything a machine would read: hosts, keys, URLs, headers, responses.
const monoFamily = 'JetBrainsMono';
const mono = TextStyle(fontFamily: monoFamily, fontSize: 12.5);

/// Status text → colour, the same meaning as on the desktop: green = open, blue = free key, amber = needs work, violet = demo key.
Color badgeColor(BuildContext context, String label) {
  final dark = Theme.of(context).brightness == Brightness.dark;
  return switch (label) {
    'Demo key' => dark ? const Color(0xFFB48CFF) : const Color(0xFF6A4FD8),
    'Open' || 'Key optional' || 'Full free access' => dark ? const Color(0xFF5AD48A) : const Color(0xFF1F8A4C),
    'Free key' || 'Free tier (limited)' => dark ? const Color(0xFF6EA2FF) : const Color(0xFF2563EB),
    'Key needed' || 'OAuth' || 'Demo / trial only' => dark ? const Color(0xFFF0B24A) : const Color(0xFFB26A00),
    _ => Theme.of(context).colorScheme.onSurfaceVariant,
  };
}

/// "Demo key · Free tier (limited)" as two coloured words - the ledger uses colour only where it means something.
class StatusLine extends StatelessWidget {
  final ApiView view;
  final bool hasKey;
  const StatusLine(this.view, {super.key, this.hasKey = false});

  @override
  Widget build(BuildContext context) {
    final muted = Theme.of(context).colorScheme.onSurfaceVariant;
    return Row(children: [
      Flexible(
        child: Text.rich(
          TextSpan(children: [
            TextSpan(text: view.keyBadge, style: TextStyle(color: badgeColor(context, view.keyBadge))),
            TextSpan(text: '  ·  ', style: TextStyle(color: muted)),
            TextSpan(text: view.accessLabel, style: TextStyle(color: badgeColor(context, view.accessLabel))),
          ]),
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(fontSize: 11.5, fontWeight: FontWeight.w700),
        ),
      ),
      if (hasKey) ...[const SizedBox(width: 8), Tooltip(message: 'Your key is saved', child: Icon(Icons.key, size: 14, color: badgeColor(context, 'Open')))],
    ]);
  }
}

const _avatarColors = [Color(0xFF4F8CFF), Color(0xFF3DDCB4), Color(0xFFB48CFF), Color(0xFFF5B83D), Color(0xFFFF7A59), Color(0xFF2BB5D9), Color(0xFFE0609A), Color(0xFF6FBF4A)];

/// The provider's site icon on a white tile, or a coloured initial while (or if) there is none.
/// With [hero] the tile flies between the list row and the detail header (one hero per key per page).
class BrandTile extends StatelessWidget {
  final ApiView view;
  final double size;
  final bool hero;
  const BrandTile(this.view, {super.key, this.size = 36, this.hero = false});

  @override
  Widget build(BuildContext context) {
    final host = view.host;
    final letter = RegExp(r'[\p{L}\p{N}]', unicode: true).firstMatch(view.name)?.group(0)?.toUpperCase() ?? '?';
    var hash = 23;
    for (final c in (host.isEmpty ? view.name : host).codeUnits) {
      hash = (hash * 31 + c) & 0x7fffffff;
    }
    final initial = Container(
      width: size,
      height: size,
      alignment: Alignment.center,
      decoration: BoxDecoration(color: _avatarColors[hash % _avatarColors.length], borderRadius: BorderRadius.circular(size * 0.18)),
      child: Text(letter, style: TextStyle(color: Colors.white, fontWeight: FontWeight.w800, fontSize: size * 0.45)),
    );
    final tile = host.isEmpty
        ? initial
        : ClipRRect(
            borderRadius: BorderRadius.circular(size * 0.18),
            child: Image.network(
              // only the domain name is sent, to a public icon service - the same one the desktop app uses
              'https://www.google.com/s2/favicons?sz=64&domain=$host',
              width: size,
              height: size,
              fit: BoxFit.contain,
              errorBuilder: (_, _, _) => initial,
              // same footprint as the initial, so rows line up whether or not an icon arrived
              frameBuilder: (_, child, frame, _) => frame == null ? initial : Container(width: size, height: size, color: Colors.white, padding: EdgeInsets.all(size * 0.08), child: child),
            ),
          );
    return hero ? Hero(tag: 'brand:${view.entry.key}', child: tile) : tile;
  }
}

/// One row of the list: a hairline below, colour only in the status words.
class ApiTile extends StatelessWidget {
  final ApiView view;
  final bool favourite;
  final VoidCallback onTap;
  final VoidCallback onFavourite;
  final List<String> tags;
  final bool hasKey;
  const ApiTile({super.key, required this.view, required this.favourite, required this.onTap, required this.onFavourite, this.tags = const [], this.hasKey = false});

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return InkWell(
      onTap: onTap,
      child: Container(
        decoration: BoxDecoration(border: Border(bottom: BorderSide(color: scheme.outlineVariant))),
        padding: const EdgeInsets.fromLTRB(16, 10, 4, 10),
        child: Row(children: [
          BrandTile(view, hero: true),
          const SizedBox(width: 12),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(view.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 15, height: 1.2)),
              if (view.entry.description.isNotEmpty)
                Text(view.entry.description, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: scheme.onSurfaceVariant, fontSize: 12.5)),
              const SizedBox(height: 4),
              StatusLine(view, hasKey: hasKey),
              if (tags.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.only(top: 3),
                  child: Text(tags.map((t) => '#$t').join('  '), maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 11.5, fontWeight: FontWeight.w600, color: scheme.primary)),
                ),
            ]),
          ),
          IconButton(
            tooltip: favourite ? 'Remove from favourites' : 'Add to favourites',
            icon: Icon(favourite ? Icons.star : Icons.star_border, color: favourite ? const Color(0xFFF5B83D) : scheme.onSurfaceVariant),
            onPressed: onFavourite,
          ),
        ]),
      ),
    );
  }
}

/// Grey rows that breathe while the first scan runs, so the screen is never blank.
class SkeletonRows extends StatefulWidget {
  final int count;
  const SkeletonRows({super.key, this.count = 9});
  @override
  State<SkeletonRows> createState() => _SkeletonRowsState();
}

class _SkeletonRowsState extends State<SkeletonRows> with SingleTickerProviderStateMixin {
  late final _pulse = AnimationController(vsync: this, duration: const Duration(milliseconds: 900))..repeat(reverse: true);

  @override
  void dispose() {
    _pulse.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    Widget block(double w, double h) => Container(width: w, height: h, decoration: BoxDecoration(color: scheme.surfaceContainerHighest, borderRadius: BorderRadius.circular(4)));
    return FadeTransition(
      opacity: Tween(begin: 0.45, end: 1.0).animate(CurvedAnimation(parent: _pulse, curve: Curves.easeInOut)),
      child: Column(children: [
        for (var i = 0; i < widget.count; i++)
          Container(
            decoration: BoxDecoration(border: Border(bottom: BorderSide(color: scheme.outlineVariant))),
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 12),
            child: Row(children: [
              block(36, 36),
              const SizedBox(width: 12),
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  block(90.0 + (i * 37) % 80, 12),
                  const SizedBox(height: 6),
                  block(double.infinity, 10),
                  const SizedBox(height: 6),
                  block(70, 9),
                ]),
              ),
            ]),
          ),
      ]),
    );
  }
}

/// A titled section: a hairline above, the title in the ledger's heavy weight, no card.
class Section extends StatelessWidget {
  final String title;
  final List<Widget> children;
  final Widget? trailing;
  final bool first;
  const Section(this.title, {super.key, required this.children, this.trailing, this.first = false});

  @override
  Widget build(BuildContext context) => Container(
        decoration: first ? null : BoxDecoration(border: Border(top: BorderSide(color: Theme.of(context).colorScheme.outlineVariant))),
        padding: EdgeInsets.fromLTRB(16, first ? 4 : 16, 16, 12),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [Expanded(child: Text(title, style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w800, letterSpacing: -0.2))), ?trailing]),
          const SizedBox(height: 6),
          ...children,
        ]),
      );
}

/// The quiet button: a tinted ground with ink text. (The theme paints every FilledButton in ink, the tonal variant included.)
ButtonStyle tonalStyle(BuildContext context) {
  final scheme = Theme.of(context).colorScheme;
  return FilledButton.styleFrom(backgroundColor: scheme.secondaryContainer, foregroundColor: scheme.onSurface, disabledBackgroundColor: scheme.secondaryContainer.withValues(alpha: 0.5));
}

/// A small accent-coloured label over a value.
class FieldLabel extends StatelessWidget {
  final String text;
  const FieldLabel(this.text, {super.key});
  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(top: 14, bottom: 4),
        child: Text(text, style: TextStyle(fontSize: 10.5, fontWeight: FontWeight.w700, letterSpacing: 1.1, color: Theme.of(context).colorScheme.primary)),
      );
}

/// A selectable monospace value in a hairline box with Copy (and optionally Open) beside it. [strong] draws the ink border used for the one value that matters most.
class ValueRow extends StatelessWidget {
  final String value;
  final VoidCallback onCopy;
  final VoidCallback? onOpen;
  final bool strong;
  const ValueRow(this.value, {super.key, required this.onCopy, this.onOpen, this.strong = false});

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Row(crossAxisAlignment: CrossAxisAlignment.center, children: [
      Expanded(
        child: Container(
          padding: EdgeInsets.symmetric(horizontal: 10, vertical: strong ? 9 : 8),
          decoration: BoxDecoration(
            border: Border.all(color: strong ? scheme.onSurface : scheme.outlineVariant, width: strong ? 1.5 : 1),
            borderRadius: BorderRadius.circular(4),
          ),
          child: SelectableText(value, style: mono.copyWith(fontSize: strong ? 15 : 12.5, fontWeight: strong ? FontWeight.w700 : FontWeight.w400)),
        ),
      ),
      IconButton(tooltip: 'Copy', icon: const Icon(Icons.copy, size: 20), onPressed: onCopy),
      if (onOpen != null) IconButton(tooltip: 'Open in the browser', icon: const Icon(Icons.open_in_new, size: 20), onPressed: onOpen),
    ]);
  }
}

/// Long instructions folded to two lines with "Show the steps"; short ones are simply shown.
class FoldedText extends StatefulWidget {
  final String text;
  final String showLabel;
  const FoldedText(this.text, {super.key, this.showLabel = 'Show the steps'});
  @override
  State<FoldedText> createState() => _FoldedTextState();
}

class _FoldedTextState extends State<FoldedText> {
  bool _open = false;

  @override
  Widget build(BuildContext context) {
    final long = widget.text.length > 150 || widget.text.contains('\n');
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Text(widget.text, maxLines: long && !_open ? 2 : null, overflow: long && !_open ? TextOverflow.ellipsis : null, style: const TextStyle(height: 1.45)),
      if (long)
        TextButton(
          style: TextButton.styleFrom(padding: EdgeInsets.zero, minimumSize: const Size(0, 32), tapTargetSize: MaterialTapTargetSize.shrinkWrap),
          onPressed: () => setState(() => _open = !_open),
          child: Text(_open ? 'Show less' : widget.showLabel),
        ),
    ]);
  }
}
