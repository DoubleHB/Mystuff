import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:url_launcher/url_launcher.dart';

import 'app_state.dart';
import 'detail_page.dart';
import 'knowledge.dart';
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
                    } else {
                      state.setTheme(ThemeModeSetting.values.byName(v));
                    }
                  },
                  itemBuilder: (_) => [
                    for (final t in ThemeModeSetting.values)
                      CheckedPopupMenuItem(value: t.name, checked: state.theme == t, child: Text('Theme: ${t.name}')),
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
                  if (state.all.isNotEmpty) Text('${state.rows.length} of ${state.all.length}', style: Theme.of(context).textTheme.labelMedium),
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
                            itemCount: state.rows.length,
                            itemBuilder: (context, i) {
                              final v = state.rows[i];
                              return ApiTile(
                                view: v,
                                favourite: state.isFavourite(v),
                                onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => DetailPage(view: v))),
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
              for (final (name, count) in state.categories)
                ListTile(
                  dense: true,
                  selected: state.category == name,
                  enabled: count > 0 || state.category == name,
                  title: Text(name, overflow: TextOverflow.ellipsis),
                  trailing: Text('$count'),
                  onTap: () {
                    state.setCategory(name);
                    Navigator.of(context).pop();
                  },
                ),
            ]),
          ),
        ),
      );
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
      applicationVersion: '1.0.0 (Android)',
      applicationLegalese: 'Free API finder. Rules and key knowledge: ${state.knowledge.exportedFrom}.',
      children: const [
        SizedBox(height: 12),
        Text('Scans five public API directories, merges and categorises them, and shows whether an API needs a key - with the '
            'provider\'s own published demo key where there is one, or how to get a key.\n\n'
            'Demo keys shown are only ones the providers print in their own docs. ApiScout never looks for leaked or private keys.'),
      ],
    );

// keeps the import of knowledge.dart honest for analyzers: the tile and detail page take ApiView
typedef ApiViewRef = ApiView;
