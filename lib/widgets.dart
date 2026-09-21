import 'package:flutter/material.dart';

import 'knowledge.dart';

/// Badge text → colour, the same meaning as on the desktop: green = open, blue = free key, amber = needs work, violet = demo key.
Color badgeColor(BuildContext context, String label) {
  final dark = Theme.of(context).brightness == Brightness.dark;
  return switch (label) {
    'Demo key' => dark ? const Color(0xFFB48CFF) : const Color(0xFF7443D6),
    'Open' || 'Key optional' || 'Full free access' => dark ? const Color(0xFF3DDC84) : const Color(0xFF12834A),
    'Free key' || 'Free tier (limited)' => dark ? const Color(0xFF4F8CFF) : const Color(0xFF2563EB),
    'Key needed' || 'OAuth' || 'Demo / trial only' => dark ? const Color(0xFFF5B83D) : const Color(0xFFA86A00),
    _ => Theme.of(context).colorScheme.onSurfaceVariant,
  };
}

class BadgeChip extends StatelessWidget {
  final String label;
  const BadgeChip(this.label, {super.key});

  @override
  Widget build(BuildContext context) {
    final c = badgeColor(context, label);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 1),
      decoration: BoxDecoration(color: c.withValues(alpha: 0.18), borderRadius: BorderRadius.circular(9)),
      child: Text(label, style: TextStyle(color: c, fontSize: 11.5, fontWeight: FontWeight.w600)),
    );
  }
}

const _avatarColors = [Color(0xFF4F8CFF), Color(0xFF3DDCB4), Color(0xFFB48CFF), Color(0xFFF5B83D), Color(0xFFFF7A59), Color(0xFF2BB5D9), Color(0xFFE0609A), Color(0xFF6FBF4A)];

/// The provider's site icon on a white tile, or a coloured initial while (or if) there is none.
class BrandTile extends StatelessWidget {
  final ApiView view;
  final double size;
  const BrandTile(this.view, {super.key, this.size = 36});

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
      decoration: BoxDecoration(color: _avatarColors[hash % _avatarColors.length], borderRadius: BorderRadius.circular(8)),
      child: Text(letter, style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: size * 0.45)),
    );
    if (host.isEmpty) return initial;
    return ClipRRect(
      borderRadius: BorderRadius.circular(8),
      child: Image.network(
        // only the domain name is sent, to a public icon service - the same one the desktop app uses
        'https://www.google.com/s2/favicons?sz=64&domain=$host',
        width: size,
        height: size,
        fit: BoxFit.contain,
        errorBuilder: (_, _, _) => initial,
        // same footprint as the initial, so rows line up whether or not an icon arrived
        frameBuilder: (_, child, frame, _) => frame == null ? initial : Container(width: size, height: size, color: Colors.white, padding: const EdgeInsets.all(3), child: child),
      ),
    );
  }
}

class ApiTile extends StatelessWidget {
  final ApiView view;
  final bool favourite;
  final VoidCallback onTap;
  final VoidCallback onFavourite;
  const ApiTile({super.key, required this.view, required this.favourite, required this.onTap, required this.onFavourite});

  @override
  Widget build(BuildContext context) {
    final muted = Theme.of(context).colorScheme.onSurfaceVariant;
    return InkWell(
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 8, 4, 8),
        child: Row(children: [
          BrandTile(view),
          const SizedBox(width: 12),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(view.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 15)),
              if (view.entry.description.isNotEmpty)
                Text(view.entry.description, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: muted, fontSize: 12.5)),
              const SizedBox(height: 4),
              Row(children: [
                BadgeChip(view.keyBadge),
                const SizedBox(width: 8),
                Flexible(child: Text(view.accessLabel, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 11.5, color: badgeColor(context, view.accessLabel)))),
              ]),
            ]),
          ),
          IconButton(
            tooltip: favourite ? 'Remove from favourites' : 'Add to favourites',
            icon: Icon(favourite ? Icons.star : Icons.star_border, color: favourite ? const Color(0xFFF5B83D) : muted),
            onPressed: onFavourite,
          ),
        ]),
      ),
    );
  }
}

/// A titled card on the detail page.
class Section extends StatelessWidget {
  final String title;
  final List<Widget> children;
  final Widget? trailing;
  const Section(this.title, {super.key, required this.children, this.trailing});

  @override
  Widget build(BuildContext context) => Card(
        margin: const EdgeInsets.fromLTRB(12, 0, 12, 12),
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [Expanded(child: Text(title, style: Theme.of(context).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w600))), ?trailing]),
            const SizedBox(height: 8),
            ...children,
          ]),
        ),
      );
}

class FieldLabel extends StatelessWidget {
  final String text;
  const FieldLabel(this.text, {super.key});
  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(top: 12, bottom: 4),
        child: Text(text, style: TextStyle(fontSize: 11.5, fontWeight: FontWeight.w600, color: Theme.of(context).colorScheme.onSurfaceVariant)),
      );
}

/// A selectable monospace value with Copy (and optionally Open) beside it.
class ValueRow extends StatelessWidget {
  final String value;
  final VoidCallback onCopy;
  final VoidCallback? onOpen;
  final bool strong;
  const ValueRow(this.value, {super.key, required this.onCopy, this.onOpen, this.strong = false});

  @override
  Widget build(BuildContext context) => Row(crossAxisAlignment: CrossAxisAlignment.center, children: [
        Expanded(
          child: Container(
            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
            decoration: BoxDecoration(color: Theme.of(context).colorScheme.surfaceContainerHighest, borderRadius: BorderRadius.circular(8)),
            child: SelectableText(value, style: TextStyle(fontFamily: 'monospace', fontSize: strong ? 16 : 12.5, fontWeight: strong ? FontWeight.bold : FontWeight.normal)),
          ),
        ),
        IconButton(tooltip: 'Copy', icon: const Icon(Icons.copy, size: 20), onPressed: onCopy),
        if (onOpen != null) IconButton(tooltip: 'Open in the browser', icon: const Icon(Icons.open_in_new, size: 20), onPressed: onOpen),
      ]);
}
