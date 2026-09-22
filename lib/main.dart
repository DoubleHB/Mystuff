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
  // edge to edge: the list scrolls under transparent system bars; the pages add the insets back themselves
  SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge);
  await state.load();
  runApp(const ApiScoutApp());
}

/// The ledger look: one ink, one hairline, one accent (the teal of the icon), and colour only where it means something.
ThemeData ledgerTheme(Brightness brightness) {
  final dark = brightness == Brightness.dark;
  final ink = dark ? const Color(0xFFE8ECF1) : const Color(0xFF111418);
  final ground = dark ? const Color(0xFF0F1114) : Colors.white;
  final line = dark ? const Color(0xFF262B33) : const Color(0xFFE3E7EC);
  final muted = dark ? const Color(0xFF98A2B0) : const Color(0xFF6B7480);
  final tint = dark ? const Color(0xFF181C22) : const Color(0xFFF3F5F7);
  final accent = dark ? const Color(0xFF3ECDB8) : const Color(0xFF158F82);
  final scheme = ColorScheme.fromSeed(seedColor: accent, brightness: brightness).copyWith(
    primary: accent,
    onPrimary: dark ? const Color(0xFF0F1114) : Colors.white,
    surface: ground,
    onSurface: ink,
    onSurfaceVariant: muted,
    outline: muted,
    outlineVariant: line,
    surfaceContainerLowest: ground,
    surfaceContainerLow: ground,
    surfaceContainer: ground,
    surfaceContainerHigh: tint,
    surfaceContainerHighest: tint,
    secondaryContainer: tint,
    onSecondaryContainer: ink,
    surfaceTint: Colors.transparent,
  );
  final overlay = SystemUiOverlayStyle(
    statusBarColor: Colors.transparent,
    statusBarIconBrightness: dark ? Brightness.light : Brightness.dark,
    systemNavigationBarColor: Colors.transparent,
    systemNavigationBarIconBrightness: dark ? Brightness.light : Brightness.dark,
    systemNavigationBarContrastEnforced: false,
  );
  const bold = TextStyle(fontFamily: 'Manrope', fontWeight: FontWeight.w700);
  return ThemeData(
    useMaterial3: true,
    colorScheme: scheme,
    fontFamily: 'Manrope',
    scaffoldBackgroundColor: ground,
    splashFactory: InkSparkle.splashFactory,
    appBarTheme: AppBarTheme(backgroundColor: ground, foregroundColor: ink, elevation: 0, scrolledUnderElevation: 0, surfaceTintColor: Colors.transparent, systemOverlayStyle: overlay),
    dividerTheme: DividerThemeData(color: line, thickness: 1, space: 1),
    cardTheme: CardThemeData(color: ground, elevation: 0, surfaceTintColor: Colors.transparent, shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(6), side: BorderSide(color: line))),
    filledButtonTheme: FilledButtonThemeData(style: FilledButton.styleFrom(backgroundColor: ink, foregroundColor: ground, shape: const StadiumBorder(), textStyle: bold)),
    outlinedButtonTheme: OutlinedButtonThemeData(style: OutlinedButton.styleFrom(foregroundColor: ink, side: BorderSide(color: ink, width: 1.5), shape: const StadiumBorder(), textStyle: bold)),
    textButtonTheme: TextButtonThemeData(style: TextButton.styleFrom(foregroundColor: accent, textStyle: bold)),
    chipTheme: ChipThemeData(
      backgroundColor: ground,
      selectedColor: ink,
      checkmarkColor: ground,
      side: BorderSide(color: line),
      shape: const StadiumBorder(),
      labelStyle: TextStyle(fontFamily: 'Manrope', fontWeight: FontWeight.w600, fontSize: 12.5, color: WidgetStateColor.resolveWith((s) => s.contains(WidgetState.selected) ? ground : ink)),
    ),
    inputDecorationTheme: InputDecorationTheme(
      isDense: true,
      border: OutlineInputBorder(borderRadius: BorderRadius.circular(4), borderSide: BorderSide(color: line)),
      enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(4), borderSide: BorderSide(color: line)),
      focusedBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(4), borderSide: BorderSide(color: ink, width: 1.5)),
      labelStyle: TextStyle(color: muted),
      helperStyle: TextStyle(color: muted, fontSize: 11),
    ),
    snackBarTheme: SnackBarThemeData(behavior: SnackBarBehavior.floating, backgroundColor: ink, contentTextStyle: TextStyle(fontFamily: 'Manrope', color: ground, fontWeight: FontWeight.w600)),
    tabBarTheme: TabBarThemeData(labelColor: ink, unselectedLabelColor: muted, indicatorColor: ink, indicatorSize: TabBarIndicatorSize.label, dividerColor: line, labelStyle: bold.copyWith(fontSize: 13), unselectedLabelStyle: bold.copyWith(fontSize: 13, fontWeight: FontWeight.w600)),
    listTileTheme: ListTileThemeData(selectedColor: ink, selectedTileColor: tint),
    drawerTheme: DrawerThemeData(backgroundColor: ground, surfaceTintColor: Colors.transparent, shape: const RoundedRectangleBorder()),
    bottomSheetTheme: BottomSheetThemeData(backgroundColor: ground, surfaceTintColor: Colors.transparent),
    dialogTheme: DialogThemeData(backgroundColor: ground, surfaceTintColor: Colors.transparent),
    popupMenuTheme: PopupMenuThemeData(color: ground, surfaceTintColor: Colors.transparent),
    progressIndicatorTheme: ProgressIndicatorThemeData(color: accent, linearTrackColor: tint),
    switchTheme: SwitchThemeData(thumbColor: WidgetStateProperty.resolveWith((s) => s.contains(WidgetState.selected) ? ground : muted), trackColor: WidgetStateProperty.resolveWith((s) => s.contains(WidgetState.selected) ? ink : tint)),
  );
}

class ApiScoutApp extends StatelessWidget {
  const ApiScoutApp({super.key});

  @override
  Widget build(BuildContext context) => ListenableBuilder(
        listenable: state,
        builder: (context, _) => MaterialApp(
          title: 'ApiScout',
          debugShowCheckedModeBanner: false,
          theme: ledgerTheme(Brightness.light),
          darkTheme: ledgerTheme(Brightness.dark),
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
    ..showSnackBar(SnackBar(content: Text('✓ $label copied'), duration: const Duration(seconds: 2)));
}

Future<void> openUrl(BuildContext context, String? url) async {
  final uri = Uri.tryParse(url ?? '');
  if (uri == null || !(uri.scheme == 'http' || uri.scheme == 'https')) return;
  final ok = await launchUrl(uri, mode: LaunchMode.externalApplication);
  if (!ok && context.mounted) ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Could not open the browser')));
}

/// "★ Favourites" → "Favourites", "📁 Side projects" → "Side projects": the headline carries the name, not the marker.
String plainCategory(String category) => category == allCategory ? 'All APIs' : category.replaceFirst(RegExp(r'^[^\p{L}\p{N}]+', unicode: true), '');

class HomePage extends StatefulWidget {
  const HomePage({super.key});
  @override
  State<HomePage> createState() => _HomePageState();
}

class _HomePageState extends State<HomePage> {
  final _search = TextEditingController();
  final _list = ScrollController();
  int _listVersion = 0;

  @override
  void dispose() {
    _search.dispose();
    _list.dispose();
    super.dispose();
  }

  String get _subtitle {
    if (state.busy || state.all.isEmpty) return state.status;
    final t = state.scannedAt;
    final when = t == null ? '' : '  ·  scanned ${t.day}/${t.month}/${t.year}';
    return '${state.rows.length} of ${state.all.length} APIs$when';
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
        listenable: state,
        builder: (context, _) {
          final scheme = Theme.of(context).colorScheme;
          if (_listVersion != state.listVersion) {
            // a new category, search, tag or filter: start the new list from the top, not wherever the old one was
            _listVersion = state.listVersion;
            WidgetsBinding.instance.addPostFrameCallback((_) {
              if (_list.hasClients && _list.offset > 0) _list.jumpTo(0);
            });
          }
          final showTags = state.all.isNotEmpty && state.user.tagCounts.isNotEmpty;
          return Scaffold(
            drawer: const CategoryDrawer(),
            body: CustomScrollView(
              controller: _list,
              slivers: [
                // floating + snap: the header slides away as the list scrolls and comes back on a flick up
                SliverAppBar(
                  floating: true,
                  snap: true,
                  toolbarHeight: 66,
                  titleSpacing: 0,
                  title: Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisSize: MainAxisSize.min, children: [
                    Text(plainCategory(state.category), maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 24, fontWeight: FontWeight.w800, letterSpacing: -0.6, height: 1.1)),
                    const SizedBox(height: 3),
                    Text(_subtitle, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 11.5, fontWeight: FontWeight.w600, color: scheme.onSurfaceVariant)),
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
                  bottom: PreferredSize(
                    preferredSize: Size.fromHeight(54 + (showTags ? 44 : 0) + (state.busy ? 3 : 0)),
                    child: Column(children: [
                      Padding(
                        padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
                        child: SizedBox(
                          height: 46,
                          child: TextField(
                            controller: _search,
                            onChanged: state.setSearch,
                            textInputAction: TextInputAction.search,
                            style: const TextStyle(fontSize: 15),
                            decoration: InputDecoration(
                              hintText: 'Search name, description, category or URL',
                              hintStyle: TextStyle(color: scheme.onSurfaceVariant, fontSize: 14.5),
                              prefixIcon: const Icon(Icons.search, size: 22),
                              prefixIconConstraints: const BoxConstraints(minWidth: 34),
                              suffixIcon: _search.text.isEmpty
                                  ? null
                                  : IconButton(
                                      icon: const Icon(Icons.close, size: 20),
                                      tooltip: 'Clear the search',
                                      onPressed: () {
                                        _search.clear();
                                        state.setSearch('');
                                      }),
                              suffixIconConstraints: const BoxConstraints(minWidth: 34),
                              contentPadding: const EdgeInsets.symmetric(vertical: 10),
                              border: UnderlineInputBorder(borderSide: BorderSide(color: scheme.onSurface, width: 1.5)),
                              enabledBorder: UnderlineInputBorder(borderSide: BorderSide(color: scheme.onSurface, width: 1.5)),
                              focusedBorder: UnderlineInputBorder(borderSide: BorderSide(color: scheme.primary, width: 2)),
                            ),
                          ),
                        ),
                      ),
                      if (showTags)
                        SizedBox(
                          height: 44,
                          child: ListView(scrollDirection: Axis.horizontal, padding: const EdgeInsets.symmetric(horizontal: 12), children: [
                            for (final (tag, count) in state.user.tagCounts)
                              Padding(
                                padding: const EdgeInsets.only(right: 6),
                                child: FilterChip(
                                  label: Text('#$tag  $count'),
                                  visualDensity: VisualDensity.compact,
                                  showCheckmark: false,
                                  selected: state.tagFilter?.toLowerCase() == tag.toLowerCase(),
                                  onSelected: (on) => state.setTagFilter(on ? tag : null),
                                ),
                              ),
                          ]),
                        ),
                      if (state.busy) LinearProgressIndicator(minHeight: 3, value: state.progress == 0 ? null : state.progress),
                    ]),
                  ),
                ),
                if (state.all.isEmpty)
                  SliverFillRemaining(hasScrollBody: false, child: state.busy ? const SkeletonRows() : EmptyState(onScan: state.scan))
                else if (state.rows.isEmpty)
                  SliverFillRemaining(
                    hasScrollBody: false,
                    child: Center(
                      child: Column(mainAxisSize: MainAxisSize.min, children: [
                        const Text('Nothing matches these filters', style: TextStyle(fontWeight: FontWeight.w600)),
                        const SizedBox(height: 10),
                        OutlinedButton(
                            onPressed: () {
                              _search.clear();
                              state.clearFilters();
                            },
                            child: const Text('Clear filters')),
                      ]),
                    ),
                  )
                else
                  SliverList.builder(
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
                SliverPadding(padding: EdgeInsets.only(bottom: 24 + MediaQuery.viewPaddingOf(context).bottom)),
              ],
            ),
          );
        },
      );
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
              'ApiScout reads the big public API directories, merges them, works out what each API is for, and tells you whether you need a key - and how to get one.',
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

// ---------------------------------------------------------------- moving data between the desktop app and the phone

void _say(BuildContext context, String text) {
  if (!context.mounted) return;
  ScaffoldMessenger.of(context)
    ..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(text), duration: const Duration(seconds: 5)));
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
            TextField(controller: first, obscureText: true, autocorrect: false, enableSuggestions: false, decoration: const InputDecoration(labelText: 'Passphrase')),
            if (confirm) ...[
              const SizedBox(height: 10),
              TextField(controller: second, obscureText: true, autocorrect: false, enableSuggestions: false, decoration: const InputDecoration(labelText: 'The same again')),
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
              const Text('Filters', style: TextStyle(fontSize: 22, fontWeight: FontWeight.w800, letterSpacing: -0.4)),
              const SizedBox(height: 14),
              DropdownButtonFormField<String>(
                initialValue: state.authFilter,
                decoration: const InputDecoration(labelText: 'What you need before you can call it'),
                items: [for (final f in authFilters) DropdownMenuItem(value: f, child: Text(f))],
                onChanged: (v) => state.setFilters(auth: v),
              ),
              const SizedBox(height: 12),
              DropdownButtonFormField<String>(
                initialValue: state.accessFilter,
                decoration: const InputDecoration(labelText: 'How much is free'),
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
                Text('${state.rows.length} APIs match', style: const TextStyle(fontWeight: FontWeight.w700)),
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
      applicationVersion: '1.3.0 (Android)',
      applicationLegalese: 'Free API finder. Rules and key knowledge: ${state.knowledge.exportedFrom}. Typefaces Manrope and JetBrains Mono, SIL Open Font License.',
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
