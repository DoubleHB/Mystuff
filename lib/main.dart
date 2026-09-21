import 'dart:convert';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:url_launcher/url_launcher.dart';

import 'app_state.dart';
import 'detail_page.dart';
import 'knowledge.dart';
import 'user_data.dart';
import 'widgets.dart';

final state = AppState();

void main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await state.load();
  runApp(const ApiScoutApp());
}

class ApiScoutApp extends StatelessWidget {
  const ApiScoutApp({super.key});

  @override
  Widget build(BuildContext context) => ListenableBuilder(
        listenable: state,
        builder: (context, _) => MaterialApp(
          title: 'ApiScout',
          debugShowCheckedModeBanner: false,
          theme: ThemeData(colorSchemeSeed: const Color(0xFF2563EB), brightness: Brightness.light, useMaterial3: true),
          darkTheme: ThemeData(colorSchemeSeed: const Color(0xFF4F8CFF), brightness: Brightness.dark, useMaterial3: true),
          themeMode: switch (state.theme) {
            ThemeModeSetting.light => ThemeMode.light,
            ThemeModeSetting.dark => ThemeMode.dark,
            ThemeModeSetting.system => ThemeMode.system,
          },
          home: const HomePage(),
        ),
      );
}

/// The search box must not take the focus (and the keyboard) back when the detail page closes.
void openDetail(BuildContext context, ApiView v) {
  FocusManager.instance.primaryFocus?.unfocus();
  Navigator.of(context).push(MaterialPageRoute(builder: (_) => DetailPage(view: v)));
}

Future<void> copyText(BuildContext context, String text, String label) async {
  if (text.isEmpty) return;
  await Clipboard.setData(ClipboardData(text: text));
  if (!context.mounted) return;
  ScaffoldMessenger.of(context)
    ..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text('✓ $label copied'), duration: const Duration(seconds: 2), behavior: SnackBarBehavior.floating));
}

Future<void> openUrl(BuildContext context, String? url) async {
  final uri = Uri.tryParse(url ?? '');
  if (uri == null || !(uri.scheme == 'http' || uri.scheme == 'https')) return;
  final ok = await launchUrl(uri, mode: LaunchMode.externalApplication);
  if (!ok && context.mounted) ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Could not open the browser')));
}

class HomePage extends StatefulWidget {
  const HomePage({super.key});
  @override
  State<HomePage> createState() => _HomePageState();
}

class _HomePageState extends State<HomePage> {
  final _search = TextEditingController();

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
        listenable: state,
        builder: (context, _) {
          final scheme = Theme.of(context).colorScheme;
          return Scaffold(
            appBar: AppBar(
              title: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                const Text('ApiScout', style: TextStyle(fontWeight: FontWeight.bold)),
                Text(state.category == allCategory ? 'free API finder' : state.category, style: Theme.of(context).textTheme.bodySmall),
              ]),
              actions: [
                IconButton(
                  tooltip: 'Filters',
                  icon: Badge(isLabelVisible: state.activeFilterCount > 0, label: Text('${state.activeFilterCount}'), child: const Icon(Icons.tune)),
                  onPressed: () => showModalBottomSheet(context: context, showDragHandle: true, isScrollControlled: true, builder: (_) => const FilterSheet()),
                ),
                IconButton(tooltip: 'Scan the internet', icon: const Icon(Icons.refresh), onPressed: state.busy ? null : state.scan),
                PopupMenuButton<String>(
                  onSelected: (v) {
                    if (v == 'about') {
                      showAbout(context);
                    } else if (v == 'import') {
                      importFromFile(context);
                    } else if (v == 'export') {
                      exportToFile(context);
                    } else {
                      state.setTheme(ThemeModeSetting.values.byName(v));
                    }
                  },
                  itemBuilder: (_) => [
                    for (final t in ThemeModeSetting.values)
                      CheckedPopupMenuItem(value: t.name, checked: state.theme == t, child: Text('Theme: ${t.name}')),
                    const PopupMenuDivider(),
                    const PopupMenuItem(value: 'import', child: Text('Import from the desktop app…')),
                    const PopupMenuItem(value: 'export', child: Text('Export my data…')),
                    const PopupMenuDivider(),
                    const PopupMenuItem(value: 'about', child: Text('About')),
                  ],
                ),
              ],
            ),
            drawer: const CategoryDrawer(),
            body: Column(children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(12, 4, 12, 6),
                child: SearchBar(
                  controller: _search,
                  hintText: 'Search name, description, category or URL',
                  leading: const Icon(Icons.search),
                  elevation: const WidgetStatePropertyAll(0),
                  trailing: [
                    if (_search.text.isNotEmpty)
                      IconButton(
                          icon: const Icon(Icons.close),
                          tooltip: 'Clear the search',
                          onPressed: () {
                            _search.clear();
                            state.setSearch('');
                          }),
                  ],
                  onChanged: state.setSearch,
                ),
              ),
              if (state.busy) LinearProgressIndicator(value: state.progress == 0 ? null : state.progress),
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
                child: Row(children: [
                  Expanded(child: Text(state.status, maxLines: 2, overflow: TextOverflow.ellipsis, style: Theme.of(context).textTheme.bodySmall?.copyWith(color: scheme.onSurfaceVariant))),
                  if (state.all.isNotEmpty) const SizedBox(width: 10),
                  if (state.all.isNotEmpty) Text('${state.rows.length} of ${state.all.length}', style: Theme.of(context).textTheme.labelMedium),
                ]),
              ),
              if (state.all.isNotEmpty && state.user.tagCounts.isNotEmpty)
                SizedBox(
                  height: 44,
                  child: ListView(scrollDirection: Axis.horizontal, padding: const EdgeInsets.symmetric(horizontal: 12), children: [
                    for (final (tag, count) in state.user.tagCounts)
                      Padding(
                        padding: const EdgeInsets.only(right: 6),
                        child: FilterChip(
                          label: Text('#$tag  $count'),
                          visualDensity: VisualDensity.compact,
                          selected: state.tagFilter?.toLowerCase() == tag.toLowerCase(),
                          onSelected: (on) => state.setTagFilter(on ? tag : null),
                        ),
                      ),
                  ]),
                ),
              Expanded(
                child: state.all.isEmpty
                    ? EmptyState(busy: state.busy, onScan: state.scan)
                    : state.rows.isEmpty
                        ? Center(
                            child: Column(mainAxisSize: MainAxisSize.min, children: [
                            const Text('Nothing matches these filters'),
                            const SizedBox(height: 8),
                            FilledButton.tonal(
                                onPressed: () {
                                  _search.clear();
                                  state.clearFilters();
                                },
                                child: const Text('Clear filters')),
                          ]))
                        : ListView.builder(
                            itemCount: state.rows.length + (state.showApiOfTheDay ? 1 : 0),
                            itemBuilder: (context, i) {
                              if (state.showApiOfTheDay) {
                                if (i == 0) return const ApiOfTheDayCard();
                                i--;
                              }
                              final v = state.rows[i];
                              return ApiTile(
                                view: v,
                                favourite: state.isFavourite(v),
                                tags: state.tagsOf(v),
                                hasKey: state.hasKey(v),
                                onTap: () => openDetail(context, v),
                                onFavourite: () => state.toggleFavourite(v),
                              );
                            },
                          ),
              ),
            ]),
          );
        },
      );
}

class EmptyState extends StatelessWidget {
  final bool busy;
  final VoidCallback onScan;
  const EmptyState({super.key, required this.busy, required this.onScan});

  @override
  Widget build(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            Text('No APIs yet', style: Theme.of(context).textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.bold)),
            const SizedBox(height: 8),
            const Text(
              'ApiScout reads the big public API directories, merges them, works out what each API is for, and tells you whether you need a key - and how to get one.',
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 20),
            FilledButton.icon(onPressed: busy ? null : onScan, icon: const Icon(Icons.refresh), label: Text(busy ? 'Scanning…' : 'Scan the internet')),
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
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
                child: Text('CATEGORIES', style: Theme.of(context).textTheme.labelMedium?.copyWith(color: Theme.of(context).colorScheme.onSurfaceVariant)),
              ),
              for (final (i, (name, count)) in state.categories.indexed) ...[
                if (i == state.specialCategoryCount) const Divider(height: 8),
                ListTile(
                  dense: true,
                  selected: state.category == name,
                  // a collection stays tappable when the filters hide all of it: long-press is how it is deleted
                  enabled: count > 0 || state.category == name || name.startsWith(collectionPrefix),
                  title: Text(name, overflow: TextOverflow.ellipsis),
                  trailing: Text('$count'),
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

/// One API a day that answers without signing up for anything.
class ApiOfTheDayCard extends StatelessWidget {
  const ApiOfTheDayCard({super.key});

  @override
  Widget build(BuildContext context) {
    final v = state.apiOfTheDay!;
    final scheme = Theme.of(context).colorScheme;
    return Card(
      margin: const EdgeInsets.fromLTRB(12, 4, 12, 8),
      color: scheme.secondaryContainer,
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: () => openDetail(context, v),
        child: Padding(
          padding: const EdgeInsets.fromLTRB(14, 10, 4, 10),
          child: Row(children: [
            BrandTile(v, size: 40),
            const SizedBox(width: 12),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text('API OF THE DAY', style: TextStyle(fontSize: 11, fontWeight: FontWeight.w700, letterSpacing: 0.6, color: scheme.onSecondaryContainer.withValues(alpha: 0.75))),
                Text(v.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontWeight: FontWeight.w700, fontSize: 16, color: scheme.onSecondaryContainer)),
                Text(v.entry.description.isEmpty ? v.entry.category : v.entry.description,
                    maxLines: 2, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 12.5, color: scheme.onSecondaryContainer)),
                const SizedBox(height: 2),
                Text('${v.hasDemoKey ? 'Demo key included' : 'No key needed'} · tap to try it', style: TextStyle(fontSize: 11.5, color: scheme.onSecondaryContainer.withValues(alpha: 0.75))),
              ]),
            ),
            IconButton(tooltip: 'Show another one', icon: Icon(Icons.refresh, color: scheme.onSecondaryContainer), onPressed: state.anotherApiOfTheDay),
          ]),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------- moving data between the desktop app and the phone

void _say(BuildContext context, String text) {
  if (!context.mounted) return;
  ScaffoldMessenger.of(context)
    ..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(text), duration: const Duration(seconds: 5), behavior: SnackBarBehavior.floating));
}

/// Asks for a passphrase. Pops null for Cancel, '' for "without the keys".
Future<String?> _askPassphrase(BuildContext context, {required String title, required String explanation, required String skipLabel, bool confirm = false}) {
  final first = TextEditingController(), second = TextEditingController();
  String? problem;
  return showDialog<String>(
    context: context,
    builder: (context) => StatefulBuilder(
      builder: (context, setState) => AlertDialog(
        title: Text(title),
        content: SingleChildScrollView(
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(explanation),
            const SizedBox(height: 14),
            TextField(controller: first, obscureText: true, autocorrect: false, enableSuggestions: false, decoration: const InputDecoration(labelText: 'Passphrase', border: OutlineInputBorder(), isDense: true)),
            if (confirm) ...[
              const SizedBox(height: 10),
              TextField(controller: second, obscureText: true, autocorrect: false, enableSuggestions: false, decoration: const InputDecoration(labelText: 'The same again', border: OutlineInputBorder(), isDense: true)),
            ],
            if (problem != null) Padding(padding: const EdgeInsets.only(top: 8), child: Text(problem!, style: TextStyle(color: Theme.of(context).colorScheme.error))),
          ]),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Cancel')),
          TextButton(onPressed: () => Navigator.of(context).pop(''), child: Text(skipLabel)),
          FilledButton(
              onPressed: () {
                if (first.text.isEmpty) {
                  setState(() => problem = 'Type the passphrase, or choose "$skipLabel".');
                } else if (confirm && first.text.length < 8) {
                  setState(() => problem = 'Use at least 8 characters: the passphrase is all that protects the keys in the file.');
                } else if (confirm && first.text != second.text) {
                  setState(() => problem = 'The two do not match.');
                } else {
                  Navigator.of(context).pop(first.text);
                }
              },
              child: const Text('OK')),
        ],
      ),
    ),
  );
}

/// Menu → Import: a file made by the desktop app's "Export my data" (or by this app).
Future<void> importFromFile(BuildContext context) async {
  try {
    final picked = await FilePicker.pickFile(dialogTitle: 'Pick the ApiScout export file', type: FileType.any);
    if (picked == null || !context.mounted) return;
    final size = await picked.length() ?? 0;
    if (!context.mounted) return;
    if (size > 20 * 1024 * 1024) return _say(context, 'That file is far too big to be an ApiScout export.');
    final json = utf8.decode(await picked.readAsBytes(), allowMalformed: true);
    final file = BackupFile.read(json); // throws when it is some other file
    String? passphrase;
    if (file.hasSecrets) {
      if (!context.mounted) return;
      passphrase = await _askPassphrase(context,
          title: 'This file holds ${file.secretCount} encrypted key(s) / test request(s)',
          explanation: 'The phone takes the keys. Type the passphrase that was chosen when the file was exported and they go into this phone\'s secure storage. Unlocking takes a few seconds.',
          skipLabel: 'Skip the keys');
      if (passphrase == null) return;
    }
    if (!context.mounted) return;
    showDialog(context: context, barrierDismissible: false, builder: (_) => const Center(child: CircularProgressIndicator()));
    ImportSummary summary;
    try {
      summary = await state.importBackup(json, passphrase);
    } finally {
      if (context.mounted) Navigator.of(context, rootNavigator: true).pop();
    }
    if (!context.mounted) return;
    showDialog(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Imported'),
        content: Text('$summary\n\nNothing that was already on this phone was changed or removed.'),
        actions: [FilledButton(onPressed: () => Navigator.of(context).pop(), child: const Text('OK'))],
      ),
    );
  } on BackupFormatException catch (ex) {
    if (context.mounted) _say(context, ex.message);
  } on WrongPassphraseException catch (ex) {
    if (context.mounted) _say(context, '$ex Nothing was imported.');
  } catch (ex) {
    if (context.mounted) _say(context, 'Could not import: $ex');
  }
}

/// Menu → Export: the same file format, so the desktop app's Import reads it.
Future<void> exportToFile(BuildContext context) async {
  if (state.userItemCount == 0 && state.keyed.isEmpty) return _say(context, 'Nothing to export yet: no favourites, tags, notes, collections, variables or keys.');
  try {
    String? passphrase = '';
    if (state.secretItemCount > 0) {
      passphrase = await _askPassphrase(context,
          title: 'Include your saved keys and request variables?',
          explanation: 'This phone holds ${state.keyed.length} key(s) and variables for ${state.user.variables.length} API(s). With a passphrase they go into the file encrypted (AES-256); the desktop app asks for the same passphrase when it imports. Without one they stay out of the file.',
          skipLabel: 'Without the keys',
          confirm: true);
      if (passphrase == null) return;
    }
    if (!context.mounted) return;
    showDialog(context: context, barrierDismissible: false, builder: (_) => const Center(child: CircularProgressIndicator()));
    String json;
    try {
      json = await state.exportBackup(passphrase);
    } finally {
      if (context.mounted) Navigator.of(context, rootNavigator: true).pop();
    }
    final now = DateTime.now();
    final saved = await FilePicker.saveFile(
      fileName: 'apiscout-phone-${now.year}-${now.month.toString().padLeft(2, '0')}-${now.day.toString().padLeft(2, '0')}.json',
      bytes: Uint8List.fromList(utf8.encode(json)),
      mimeType: 'application/json',
    );
    if (saved != null && context.mounted) _say(context, '✓ Exported. On the PC: ApiScout → About → Import…, and pick this file.');
  } catch (ex) {
    if (context.mounted) _say(context, 'Could not export: $ex');
  }
}

class FilterSheet extends StatelessWidget {
  const FilterSheet({super.key});

  @override
  Widget build(BuildContext context) => ListenableBuilder(
        listenable: state,
        builder: (context, _) => SafeArea(
          child: Padding(
            padding: EdgeInsets.fromLTRB(20, 0, 20, 16 + MediaQuery.of(context).viewInsets.bottom),
            child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text('Filters', style: Theme.of(context).textTheme.titleLarge),
              const SizedBox(height: 12),
              DropdownButtonFormField<String>(
                initialValue: state.authFilter,
                decoration: const InputDecoration(labelText: 'What you need before you can call it', border: OutlineInputBorder()),
                items: [for (final f in authFilters) DropdownMenuItem(value: f, child: Text(f))],
                onChanged: (v) => state.setFilters(auth: v),
              ),
              const SizedBox(height: 12),
              DropdownButtonFormField<String>(
                initialValue: state.accessFilter,
                decoration: const InputDecoration(labelText: 'How much is free', border: OutlineInputBorder()),
                items: [for (final f in accessFilters) DropdownMenuItem(value: f, child: Text(f))],
                onChanged: (v) => state.setFilters(access: v),
              ),
              SwitchListTile(contentPadding: EdgeInsets.zero, title: const Text('HTTPS only'), value: state.httpsOnly, onChanged: (v) => state.setFilters(https: v)),
              SwitchListTile(
                  contentPadding: EdgeInsets.zero,
                  title: const Text('CORS enabled'),
                  subtitle: const Text('Callable straight from browser JavaScript'),
                  value: state.corsOnly,
                  onChanged: (v) => state.setFilters(cors: v)),
              const SizedBox(height: 8),
              Row(children: [
                Text('${state.rows.length} APIs match', style: Theme.of(context).textTheme.labelLarge),
                const Spacer(),
                TextButton(onPressed: () => state.setFilters(auth: authFilters.first, access: accessFilters.first, https: false, cors: false), child: const Text('Clear')),
                const SizedBox(width: 8),
                FilledButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Show')),
              ]),
            ]),
          ),
        ),
      );
}

void showAbout(BuildContext context) => showAboutDialog(
      context: context,
      applicationName: 'ApiScout',
      applicationVersion: '1.2.0 (Android)',
      applicationLegalese: 'Free API finder. Rules and key knowledge: ${state.knowledge.exportedFrom}.',
      children: const [
        SizedBox(height: 12),
        Text('Scans five public API directories, merges and categorises them, and shows whether an API needs a key - with the '
            'provider\'s own published demo key where there is one, or how to get a key.\n\n'
            'Demo keys shown are only ones the providers print in their own docs. ApiScout never looks for leaked or private keys.\n\n'
            'Your own keys are kept encrypted by the Android keystore, on this phone only. Favourites, tags, notes and collections move '
            'between the PC and the phone with Export / Import (this menu; on the PC: About → Export… / Import…) - keys travel only inside the file, encrypted with a passphrase you choose.'),
      ],
    );

// keeps the import of knowledge.dart honest for analyzers: the tile and detail page take ApiView
typedef ApiViewRef = ApiView;
