// The pieces of the home page that are not the list itself: filter pills, the strips above the list, the API of the
// day, the category drawer, the empty state and the pick bar.
import 'package:flutter/material.dart';

import 'app_state.dart';
import 'changes_page.dart';
import 'compare.dart';
import 'knowledge.dart';
import 'main.dart';
import 'widgets.dart';

/// One line of a menu. Every line keeps the same slot for a tick, so ticked, unticked and plain lines start at the
/// same place (Material's CheckedPopupMenuItem indents only its own kind).
PopupMenuItem<String> menuRow(String value, String text, {bool checked = false, bool enabled = true}) => PopupMenuItem(
      value: value,
      enabled: enabled,
      child: Semantics(
        checked: checked ? true : null,
        child: Row(children: [
          SizedBox(width: 30, child: checked ? const Icon(Icons.check, size: 18) : null),
          Flexible(child: Text(text)),
        ]),
      ),
    );

/// "★ Favourites" → "Favourites", "📁 Side projects" → "Side projects": the headline carries the name, not the marker.
String plainCategory(String category) => category == allCategory ? 'All APIs' : category.replaceFirst(RegExp(r'^[^\p{L}\p{N}]+', unicode: true), '');

/// One filter chip in the ledger style: a hairline pill, ink-filled while it is on. Without [onTap] the parent handles the tap.
class FilterPill extends StatelessWidget {
  final String label;
  final bool selected;
  final IconData? icon;
  final VoidCallback? onTap;
  final VoidCallback? onLongPress;

  /// The pill opens a list of choices: a small arrow follows the label (an icon - the typeface has no ▾ of its own).
  final bool menu;
  const FilterPill(this.label, {super.key, this.selected = false, this.icon, this.onTap, this.onLongPress, this.menu = false});

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.only(right: 6, top: 6, bottom: 8),
      child: Material(
        color: selected ? scheme.onSurface : Colors.transparent,
        shape: StadiumBorder(side: BorderSide(color: selected ? scheme.onSurface : scheme.outlineVariant)),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          onLongPress: onLongPress,
          child: Semantics(
            selected: selected,
            child: Padding(
              padding: EdgeInsets.fromLTRB(12, 5, menu ? 6 : 12, 5),
              child: Row(mainAxisSize: MainAxisSize.min, children: [
                if (icon != null) Padding(padding: const EdgeInsets.only(right: 4), child: Icon(icon, size: 14, color: selected ? scheme.surface : scheme.onSurface)),
                Text(label, style: TextStyle(fontSize: 12.5, fontWeight: FontWeight.w700, color: selected ? scheme.surface : scheme.onSurface)),
                if (menu) Icon(Icons.arrow_drop_down, size: 18, color: selected ? scheme.surface : scheme.onSurface),
              ]),
            ),
          ),
        ),
      ),
    );
  }
}

/// A pill that opens a menu of choices; the pill shows the choice while one is made.
class MenuPill extends StatelessWidget {
  final String name;
  final List<String> choices;
  final String current;
  final void Function(String) onPick;
  const MenuPill({super.key, required this.name, required this.choices, required this.current, required this.onPick});

  @override
  Widget build(BuildContext context) {
    final active = current != choices.first;
    return PopupMenuButton<String>(
      tooltip: name,
      onSelected: onPick,
      itemBuilder: (_) => [for (final c in choices) menuRow(c, c, checked: c == current)],
      child: FilterPill(active ? current : name, menu: true, selected: active),
    );
  }
}

/// The bar that floats over the list while APIs are being picked: how many, Compare, and a way out.
class PickBar extends StatelessWidget {
  final int count;
  final VoidCallback? onCompare;
  final VoidCallback onCancel;
  const PickBar({super.key, required this.count, required this.onCompare, required this.onCancel});

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final more = compareMin - count;
    return Material(
      color: scheme.onSurface,
      elevation: 4,
      borderRadius: BorderRadius.circular(8),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(4, 6, 8, 6),
        child: Row(children: [
          IconButton(tooltip: 'Stop picking', icon: Icon(Icons.close, color: scheme.surface), onPressed: onCancel),
          Expanded(
            child: Text(
              more > 0 ? '$count picked  ·  tap ${more == 1 ? 'one more' : '$more more'} to compare' : '$count picked${count < compareMax ? '  ·  up to $compareMax' : ''}',
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(color: scheme.surface, fontWeight: FontWeight.w700, fontSize: 13),
            ),
          ),
          FilledButton(
            style: FilledButton.styleFrom(backgroundColor: scheme.surface, foregroundColor: scheme.onSurface, disabledBackgroundColor: scheme.surface.withValues(alpha: 0.25), disabledForegroundColor: scheme.surface.withValues(alpha: 0.6)),
            onPressed: onCompare,
            child: const Text('Compare'),
          ),
        ]),
      ),
    );
  }
}

/// "12 new, 3 gone since the last scan · See": one line above the list until the user has looked or dismissed it.
class ChangesStrip extends StatelessWidget {
  const ChangesStrip({super.key});

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final c = state.changes!;
    return Material(
      color: scheme.surfaceContainerHighest,
      child: InkWell(
        onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => ChangesPage(diff: c))),
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 6, 4, 6),
          child: Row(children: [
            Icon(Icons.new_releases_outlined, size: 18, color: scheme.primary),
            const SizedBox(width: 10),
            Expanded(child: Text('${c.summary} since the last scan  ·  See', maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 12.5, fontWeight: FontWeight.w700))),
            IconButton(tooltip: 'Dismiss', visualDensity: VisualDensity.compact, icon: const Icon(Icons.close, size: 18), onPressed: state.markChangesSeen),
          ]),
        ),
      ),
    );
  }
}

/// A file the app could not read at start-up was set aside: said once, above the list, until dismissed.
class DataProblemStrip extends StatelessWidget {
  const DataProblemStrip({super.key});

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Material(
      color: scheme.errorContainer,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 6, 4, 6),
        child: Row(children: [
          Icon(Icons.warning_amber_rounded, size: 18, color: scheme.error),
          const SizedBox(width: 10),
          Expanded(child: Text(state.dataProblem ?? '', style: TextStyle(fontSize: 12.5, fontWeight: FontWeight.w600, height: 1.3, color: scheme.onErrorContainer))),
          IconButton(tooltip: 'Dismiss', visualDensity: VisualDensity.compact, icon: const Icon(Icons.close, size: 18), onPressed: state.dismissDataProblem),
        ]),
      ),
    );
  }
}

/// Swipe left on a row, or the button on the detail page → pick a collection for the API.
Future<void> addToCollectionFor(BuildContext context, ApiView v) async {
  final name = await showDialog<String>(context: context, builder: (_) => CollectionDialog(already: state.collectionsOf(v)));
  if (name == null || name.trim().isEmpty || !context.mounted) return;
  final used = state.addToCollection(v, name);
  ScaffoldMessenger.of(context)
    ..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text('✓ ${v.name} added to "$used" - it is in the category menu')));
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
            decoration: InputDecoration(labelText: others.isEmpty ? 'Name of the collection' : 'Or a new collection'),
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

class EmptyState extends StatelessWidget {
  final VoidCallback onScan;
  const EmptyState({super.key, required this.onScan});

  @override
  Widget build(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            const Text('No APIs yet', style: TextStyle(fontSize: 22, fontWeight: FontWeight.w800, letterSpacing: -0.4)),
            const SizedBox(height: 8),
            Text(
              'API Free reads the big public API directories, merges them, works out what each API is for, and tells you whether you need a key - and how to get one.',
              textAlign: TextAlign.center,
              style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant, height: 1.45),
            ),
            const SizedBox(height: 20),
            FilledButton.icon(onPressed: onScan, icon: const Icon(Icons.refresh), label: const Text('Scan the internet')),
          ]),
        ),
      );
}

class CategoryDrawer extends StatelessWidget {
  const CategoryDrawer({super.key});

  @override
  Widget build(BuildContext context) => Drawer(
        child: SafeArea(
          child: ListenableBuilder(
            listenable: state,
            builder: (context, _) => ListView(padding: EdgeInsets.zero, children: [
              const Padding(padding: EdgeInsets.fromLTRB(16, 18, 16, 4), child: FieldLabel('CATEGORIES')),
              for (final (i, (name, count)) in state.categories.indexed) ...[
                if (i == state.specialCategoryCount) const Divider(height: 8),
                ListTile(
                  dense: true,
                  selected: state.category == name,
                  // a collection stays tappable when the filters hide all of it: long-press is how it is deleted
                  enabled: count > 0 || state.category == name || name.startsWith(collectionPrefix),
                  title: Text(name, overflow: TextOverflow.ellipsis, style: TextStyle(fontWeight: state.category == name ? FontWeight.w800 : FontWeight.w500)),
                  trailing: Text('$count', style: TextStyle(fontFeatures: const [FontFeature.tabularFigures()], color: Theme.of(context).colorScheme.onSurfaceVariant)),
                  onTap: () {
                    state.setCategory(name);
                    Navigator.of(context).pop();
                  },
                  onLongPress: name.startsWith(collectionPrefix) ? () => _deleteCollection(context, name.substring(collectionPrefix.length)) : null,
                ),
              ],
              if (state.user.collections.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
                  child: Text('Long-press a 📁 collection to delete it.', style: Theme.of(context).textTheme.bodySmall?.copyWith(color: Theme.of(context).colorScheme.onSurfaceVariant)),
                ),
            ]),
          ),
        ),
      );
}

Future<void> _deleteCollection(BuildContext context, String name) async {
  final yes = await showDialog<bool>(
    context: context,
    builder: (_) => AlertDialog(
      title: Text('Delete "$name"?'),
      content: const Text('Only the collection goes - the APIs in it, their tags, notes and keys stay.'),
      actions: [
        TextButton(onPressed: () => Navigator.of(context).pop(false), child: const Text('Cancel')),
        FilledButton(onPressed: () => Navigator.of(context).pop(true), child: const Text('Delete')),
      ],
    ),
  );
  if (yes == true) state.deleteCollection(name);
}

/// One API a day that answers without signing up for anything: an ink-ruled box at the top of the list.
class ApiOfTheDayCard extends StatelessWidget {
  const ApiOfTheDayCard({super.key});

  @override
  Widget build(BuildContext context) {
    final v = state.apiOfTheDay!;
    final scheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 6, 16, 10),
      child: Material(
        color: Colors.transparent,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(6), side: BorderSide(color: scheme.onSurface, width: 1.5)),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: () => openDetail(context, v),
          child: Padding(
            padding: const EdgeInsets.fromLTRB(14, 10, 4, 10),
            child: Row(children: [
              // no hero here: the same API can sit in the list below, and one page can carry one hero per key
              BrandTile(v, size: 40),
              const SizedBox(width: 12),
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text('API OF THE DAY', style: TextStyle(fontSize: 10.5, fontWeight: FontWeight.w700, letterSpacing: 1.1, color: scheme.primary)),
                  Text(v.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 16, letterSpacing: -0.2)),
                  Text(v.entry.description.isEmpty ? v.entry.category : v.entry.description, maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 12.5)),
                  const SizedBox(height: 2),
                  Text('${v.hasDemoKey ? 'Demo key included' : 'No key needed'}  ·  tap to try it', style: TextStyle(fontSize: 11.5, fontWeight: FontWeight.w600, color: scheme.onSurfaceVariant)),
                ]),
              ),
              IconButton(tooltip: 'Show another one', icon: const Icon(Icons.refresh), onPressed: state.anotherApiOfTheDay),
            ]),
          ),
        ),
      ),
    );
  }
}

void showAbout(BuildContext context) => showAboutDialog(
      context: context,
      applicationName: 'API Free',
      applicationVersion: '1.6.1 (Android)',
      // the launcher icon, drawn: the blue-to-teal tile with the braces
      applicationIcon: Container(
        width: 44,
        height: 44,
        alignment: Alignment.center,
        decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(10),
            gradient: const LinearGradient(begin: Alignment.topLeft, end: Alignment.bottomRight, colors: [Color(0xFF4A8CF5), Color(0xFF3ECDB8)])),
        child: const Text('{ }', style: TextStyle(color: Colors.white, fontSize: 18, fontWeight: FontWeight.w700, height: 1)),
      ),
      applicationLegalese: 'Find and test public APIs. Rules and key knowledge: ${state.knowledge.exportedFrom}. Typefaces Manrope and JetBrains Mono, SIL Open Font License.',
      children: const [
        SizedBox(height: 12),
        Text('Scans five public API directories, merges and categorises them, and shows whether an API needs a key - with the '
            'provider\'s own published demo key where there is one, or how to get a key.\n\n'
            'Demo keys shown are only ones the providers print in their own docs. API Free never looks for leaked or private keys.\n\n'
            'Your own keys are kept encrypted by the Android keystore, on this phone only. Favourites, tags, notes and collections move '
            'between the PC and the phone with Export / Import (this menu; on the PC: About → Export… / Import…) - keys travel only inside the file, encrypted with a passphrase you choose.'),
      ],
    );
