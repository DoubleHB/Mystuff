import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart' show RenderAbstractViewport, RenderSliver;
import 'package:flutter/services.dart';
import 'package:url_launcher/url_launcher.dart';
import 'package:workmanager/workmanager.dart';

import 'app_state.dart';
import 'az_rail.dart';
import 'background.dart';
import 'changes_page.dart';
import 'code_page.dart';
import 'compare.dart';
import 'compare_page.dart';
import 'detail_page.dart';
import 'home_widgets.dart';
import 'knowledge.dart';
import 'links.dart';
import 'notify.dart';
import 'search.dart';
import 'theme.dart';
import 'transfer.dart';
import 'widgets.dart';

export 'home_widgets.dart';
export 'theme.dart';

final state = AppState();

/// The app's navigator, for pages opened from outside a screen: a notification tap or an apiscout:// link.
final appNavigator = GlobalKey<NavigatorState>();

/// "Compare with…" on an API's page: its key is put here, and the list picks it up and starts picking.
final comparePick = ValueNotifier<String?>(null);

void main() async {
  WidgetsFlutterBinding.ensureInitialized();
  // edge to edge: the list scrolls under transparent system bars; the pages add the insets back themselves
  SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge);
  try {
    await Workmanager().initialize(backgroundDispatcher);
  } catch (_) {
    // no WorkManager (a test host, say): the weekly rescan setting simply cannot be turned on
  }
  await DailyNotice.init(openApiByKey);
  await state.load();
  runApp(const ApiScoutApp());
  // a fresh install starts the first scan itself: the list fills in while the explanation shows, no button to find
  if (state.firstLaunch) state.scan();
  final fromNotification = await DailyNotice.launchedWith();
  if (fromNotification != null) WidgetsBinding.instance.addPostFrameCallback((_) => openApiByKey(fromNotification));
}

/// Opens the detail page for a catalogue key, or a note that the catalogue does not have it (yet).
void openApiByKey(String key) {
  final nav = appNavigator.currentState;
  if (nav == null) return;
  FocusManager.instance.primaryFocus?.unfocus();
  final v = state.find(key);
  nav.push(MaterialPageRoute(builder: (_) => v == null ? MissingApiPage(apiKey: key) : DetailPage(view: v)));
}

/// A link or notification pointed at an API the phone's catalogue does not hold.
class MissingApiPage extends StatelessWidget {
  final String apiKey;
  const MissingApiPage({super.key, required this.apiKey});

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(toolbarHeight: 48),
        body: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            const Text('Not in this phone\'s catalogue', style: TextStyle(fontSize: 22, fontWeight: FontWeight.w800, letterSpacing: -0.4)),
            const SizedBox(height: 10),
            Text('The link points at "$apiKey". ${state.all.isEmpty ? 'Scan first, then open the link again.' : 'It is not in the last scan on this phone - scan again, or search for it by name.'}', style: const TextStyle(height: 1.45)),
            const SizedBox(height: 20),
            FilledButton.icon(
                onPressed: () {
                  Navigator.of(context).pop();
                  state.scan();
                },
                icon: const Icon(Icons.refresh),
                label: const Text('Scan the internet')),
          ]),
        ),
      );
}

/// A link's page: the API, or a note that the catalogue does not have it.
Route<dynamic> _routeForKey(String key, RouteSettings settings) {
  final v = state.find(key);
  return MaterialPageRoute(settings: settings, builder: (_) => v == null ? MissingApiPage(apiKey: key) : DetailPage(view: v));
}

class ApiScoutApp extends StatelessWidget {
  const ApiScoutApp({super.key});

  // the themes are built once: a scan tick or a keystroke must not rebuild MaterialApp and both colour schemes
  static final _light = ledgerTheme(Brightness.light), _dark = ledgerTheme(Brightness.dark);

  @override
  Widget build(BuildContext context) => ValueListenableBuilder(
        // only the theme setting reaches up here; everything else is listened to by the pages that show it
        valueListenable: state.themeMode,
        builder: (context, theme, _) => MaterialApp(
          title: 'API Free',
          debugShowCheckedModeBanner: false,
          navigatorKey: appNavigator,
          // apiscout://open/api/<key> arrives as the route "/api/<key>", at start-up or while the app is running
          onGenerateRoute: (settings) {
            final key = apiKeyFromRoute(settings.name);
            return key == null ? null : _routeForKey(key, settings);
          },
          // a cold start on a link: the home page and that one page - not, as Flutter would do by default, one page
          // for every "/" in the key as well ("thecocktaildb.com/api.php" would get a stray "thecocktaildb.com" page)
          onGenerateInitialRoutes: (initialRoute) {
            final key = apiKeyFromRoute(initialRoute);
            return [
              MaterialPageRoute(settings: const RouteSettings(name: '/'), builder: (_) => const HomePage()),
              if (key != null) _routeForKey(key, RouteSettings(name: initialRoute)),
            ];
          },
          onUnknownRoute: (settings) => MaterialPageRoute(settings: settings, builder: (_) => const HomePage()),
          theme: _light,
          darkTheme: _dark,
          themeMode: switch (theme) {
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

class HomePage extends StatefulWidget {
  const HomePage({super.key});
  @override
  State<HomePage> createState() => _HomePageState();
}

class _HomePageState extends State<HomePage> {
  final _search = TextEditingController();
  final _searchFocus = FocusNode();
  final _list = ScrollController();
  Timer? _searchTimer;
  int _listVersion = 0;
  double _rowExtent = 76;

  // the A-Z rail: which row each initial starts at (for the rows list it was built from), the sliver that holds the
  // rows (its preceding scroll extent = header + API of the day, whatever they measure), and the initial at the top
  final _rowsKey = GlobalKey();
  List<ApiView> _railRows = const [];
  List<(String, int)> _railFirst = const [];
  final _atLetter = ValueNotifier<String?>(null);

  /// The APIs picked for a comparison, by key, in the order they were picked. Not empty = picking: a tap ticks.
  List<String> _picked = const [];

  @override
  void initState() {
    super.initState();
    _list.addListener(_onScroll);
    _searchFocus.addListener(() => setState(() {})); // the recent searches show while the box is focused and empty
    comparePick.addListener(_onComparePick);
  }

  @override
  void dispose() {
    comparePick.removeListener(_onComparePick);
    _searchTimer?.cancel();
    _search.dispose();
    _searchFocus.dispose();
    _list.dispose();
    _atLetter.dispose();
    super.dispose();
  }

  /// Typing waits for a short pause before the list is filtered: the typo-forgiving search over ~4,000 rows is not
  /// something to run on every keystroke.
  void _onSearchChanged(String text) {
    setState(() {}); // the clear button follows the box at once
    _searchTimer?.cancel();
    _searchTimer = Timer(const Duration(milliseconds: 150), () {
      if (mounted) state.setSearch(text);
    });
  }

  void _onComparePick() {
    final key = comparePick.value;
    if (key == null) return;
    comparePick.value = null;
    if (!_picked.contains(key)) {
      final next = togglePick(_picked, key);
      if (next != null) setState(() => _picked = next);
    }
  }

  void _togglePick(ApiView v) {
    final next = togglePick(_picked, v.entry.key);
    if (next == null) {
      ScaffoldMessenger.of(context)
        ..hideCurrentSnackBar()
        ..showSnackBar(const SnackBar(content: Text('A comparison takes up to $compareMax APIs'), duration: Duration(seconds: 2)));
      return;
    }
    HapticFeedback.selectionClick();
    setState(() => _picked = next);
  }

  void _compare() {
    // a rescan may have dropped a picked API
    final apis = [for (final k in _picked) ?state.find(k)];
    if (apis.length < compareMin) return;
    FocusManager.instance.primaryFocus?.unfocus();
    Navigator.of(context).push(MaterialPageRoute(builder: (_) => ComparePage(apis: apis)));
  }

  /// A category, tag or filter chip is narrowing the list (the search box is separate).
  bool get _filtered => state.activeFilterCount > 0 || state.category != allCategory || state.tagFilter != null;

  bool get _showRecent => _searchFocus.hasFocus && _search.text.trim().isEmpty && state.recentSearches.isNotEmpty;

  void _useRecent(String term) {
    _search.text = term;
    state.setSearch(term);
    state.rememberSearch(term);
    _searchFocus.unfocus();
  }

  /// A rail is worth having for a long list in name order - not for a collection, which keeps the order it was built in.
  bool get _showRail => state.rows.length >= 40 && !state.category.startsWith(collectionPrefix) && state.sort == sortChoices.first;

  List<(String, int)> get _letters {
    if (!identical(_railRows, state.rows)) {
      _railRows = state.rows;
      _railFirst = firstIndexByInitial([for (final v in state.rows) v.name]);
    }
    return _railFirst;
  }

  double get _rowsBase {
    final r = _rowsKey.currentContext?.findRenderObject();
    if (r is! RenderSliver || !r.attached || r.geometry == null) return 0;
    // the scroll offset that puts the rows sliver at the top = everything before it, at whatever height it measured
    return RenderAbstractViewport.maybeOf(r)?.getOffsetToReveal(r, 0).offset ?? 0;
  }

  void _onScroll() {
    if (!_list.hasClients || !_showRail) {
      _atLetter.value = null;
      return;
    }
    // the list runs edge to edge, so the first row the eye sees starts below the status bar
    final i = ((_list.offset + _topInset - _rowsBase) / _rowExtent).floor().clamp(0, state.rows.length - 1);
    _atLetter.value = initialOf(state.rows[i].name);
  }

  double get _topInset => MediaQuery.viewPaddingOf(context).top;

  void _jumpToLetter(String letter) {
    if (!_list.hasClients) return;
    for (final (l, i) in _letters) {
      if (l != letter) continue;
      _list.jumpTo((_rowsBase + i * _rowExtent - _topInset).clamp(0.0, _list.position.maxScrollExtent));
      return;
    }
  }

  String get _subtitle {
    if (state.busy || state.all.isEmpty) return state.status;
    final t = state.scannedAt;
    final when = t == null ? '' : '  ·  scanned ${t.day}/${t.month}/${t.year}';
    return '${state.rows.length} of ${state.all.length} APIs$when';
  }

  void _onMenu(String v) {
    switch (v) {
      case 'about':
        showAbout(context);
      case 'import':
        importFromFile(context);
      case 'export':
        exportToFile(context);
      case 'notice':
        state.setDailyNotice(!state.dailyNotice);
      case 'weekly':
        state.setWeeklyRescan(!state.weeklyRescan);
      case 'logos':
        state.setShowLogos(!state.showLogos);
      case 'client':
        collectionClient(context);
      case 'changes':
        Navigator.of(context).push(MaterialPageRoute(builder: (_) => ChangesPage(diff: state.changes!)));
      default:
        state.setTheme(ThemeModeSetting.values.byName(v));
    }
  }

  List<PopupMenuEntry<String>> _menu() => [
        for (final t in ThemeModeSetting.values) menuRow(t.name, 'Theme: ${t.name}', checked: state.theme == t),
        const PopupMenuDivider(),
        menuRow('notice', 'API of the day at 9:00 (notification)', checked: state.dailyNotice),
        menuRow('weekly', 'Rescan weekly on Wi-Fi', checked: state.weeklyRescan),
        menuRow('logos', 'Provider logos (host names go to an icon service)', checked: state.showLogos),
        menuRow('changes', 'What the last scan changed…', enabled: state.changes != null),
        const PopupMenuDivider(),
        // only while a collection is on screen: one C# file with a client per tested API in it
        if (state.category.startsWith(collectionPrefix)) menuRow('client', 'C# client for this collection…'),
        menuRow('import', 'Import from the desktop app…'),
        menuRow('export', 'Export my data…'),
        const PopupMenuDivider(),
        menuRow('about', 'About'),
      ];

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
          final showChips = state.all.isNotEmpty;
          final showRail = _showRail;
          final showRecent = _showRecent;
          if (showRail) WidgetsBinding.instance.addPostFrameCallback((_) => _onScroll());
          // everything in the header is text, so the header grows with the phone's text size
          final ts = MediaQuery.textScalerOf(context).scale(10) / 10;
          final toolbarH = 20 + 46 * ts, searchH = 8 + 46 * ts, chipsH = 44 * ts, recentH = 40 * ts;
          final headerH = toolbarH + searchH + (showChips ? chipsH : 0) + (showRecent ? recentH : 0) + (state.busy ? 3 : 0);
          _rowExtent = rowExtentFor(context);
          final picking = _picked.isNotEmpty;
          // Back ends the picking before it leaves the app
          return PopScope(
            canPop: !picking,
            onPopInvokedWithResult: (didPop, _) {
              if (!didPop) setState(() => _picked = const []);
            },
            child: Scaffold(
              drawer: const CategoryDrawer(),
              body: Stack(children: [
                CustomScrollView(
                  controller: _list,
                  slivers: [
                    // floating + snap: the header slides away as the list scrolls and comes back on a flick up
                    SliverAppBar(
                      floating: true,
                      snap: true,
                      toolbarHeight: toolbarH,
                      titleSpacing: 0,
                      title: Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisSize: MainAxisSize.min, children: [
                        Text(plainCategory(state.category), maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 24, fontWeight: FontWeight.w800, letterSpacing: -0.6, height: 1.1)),
                        const SizedBox(height: 3),
                        Text(_subtitle, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 11.5, fontWeight: FontWeight.w600, color: scheme.onSurfaceVariant)),
                      ]),
                      actions: [
                        IconButton(tooltip: 'Scan the internet', icon: const Icon(Icons.refresh), onPressed: state.busy ? null : state.scan),
                        PopupMenuButton<String>(onSelected: _onMenu, itemBuilder: (_) => _menu()),
                      ],
                      bottom: PreferredSize(
                        preferredSize: Size.fromHeight(headerH - toolbarH),
                        child: Column(children: [
                          Padding(
                            padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
                            child: SizedBox(
                              height: 46 * ts,
                              child: TextField(
                                controller: _search,
                                focusNode: _searchFocus,
                                onChanged: _onSearchChanged,
                                onSubmitted: (term) {
                                  _searchTimer?.cancel();
                                  state.setSearch(term);
                                  state.rememberSearch(term);
                                },
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
                                            _searchTimer?.cancel();
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
                          if (showRecent)
                            SizedBox(
                              height: recentH,
                              child: ListView(scrollDirection: Axis.horizontal, padding: const EdgeInsets.symmetric(horizontal: 12), children: [
                                for (final r in state.recentSearches)
                                  FilterPill(r, icon: Icons.history, onTap: () => _useRecent(r), onLongPress: () => state.forgetSearch(r)),
                                if (state.recentSearches.isNotEmpty) FilterPill('Forget all', onTap: state.clearRecentSearches),
                              ]),
                            ),
                          if (showChips) SizedBox(height: chipsH, child: _filterBar(context)),
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
                          child: Padding(
                            padding: const EdgeInsets.symmetric(horizontal: 32),
                            child: Column(mainAxisSize: MainAxisSize.min, children: [
                              // say what is in the way: the words typed, the filters, or both
                              Text(noMatchText(state.search, filtered: _filtered), textAlign: TextAlign.center, style: const TextStyle(fontWeight: FontWeight.w600)),
                              const SizedBox(height: 10),
                              OutlinedButton(
                                  onPressed: () {
                                    _searchTimer?.cancel();
                                    _search.clear();
                                    state.clearFilters();
                                  },
                                  child: Text(state.search.trim().isEmpty
                                      ? 'Clear filters'
                                      : _filtered
                                          ? 'Clear search and filters'
                                          : 'Clear the search')),
                            ]),
                          ),
                        ),
                      )
                    else ...[
                      if (state.dataProblem != null) const SliverToBoxAdapter(child: DataProblemStrip()),
                      if (state.hasUnseenChanges) const SliverToBoxAdapter(child: ChangesStrip()),
                      if (state.showApiOfTheDay) const SliverToBoxAdapter(child: ApiOfTheDayCard()),
                      // fixed-height rows: the rail can then land on row i without ever laying out the rows before it
                      SliverFixedExtentList.builder(
                        key: _rowsKey,
                        itemExtent: _rowExtent,
                        itemCount: state.rows.length,
                        itemBuilder: (context, i) {
                          final v = state.rows[i];
                          return SwipeActions(
                            rowKey: ValueKey('row:${v.entry.key}'),
                            favourite: state.isFavourite(v),
                            onStar: () => state.toggleFavourite(v),
                            onCollect: () => addToCollectionFor(context, v),
                            onPick: () => _togglePick(v),
                            child: ApiTile(
                              view: v,
                              favourite: state.isFavourite(v),
                              tags: state.tagsOf(v),
                              hasKey: state.hasKey(v),
                              rightInset: showRail ? railWidth : 0,
                              picked: picking ? _picked.contains(v.entry.key) : null,
                              // a long press starts picking APIs to compare; while picking, a tap ticks
                              onLongPress: () => _togglePick(v),
                              onTap: () {
                                if (picking) return _togglePick(v);
                                // a search that led to an API is one worth offering again
                                if (state.search.trim().isNotEmpty) state.rememberSearch(state.search);
                                openDetail(context, v);
                              },
                              onFavourite: () => state.toggleFavourite(v),
                            ),
                          );
                        },
                      ),
                    ],
                    // while picking, the last rows must be able to scroll clear of the bar
                    SliverPadding(padding: EdgeInsets.only(bottom: (picking ? 96 : 24) + MediaQuery.viewPaddingOf(context).bottom)),
                  ],
                ),
                // once the header has floated away the rows would run under the clock: a band in the ground colour
                Positioned(top: 0, left: 0, right: 0, height: MediaQuery.viewPaddingOf(context).top, child: IgnorePointer(child: ColoredBox(color: scheme.surface))),
                if (showRail)
                  Positioned(
                    right: 0,
                    width: railWidth,
                    // below the header at its full height, so the letters never sit over the search or the chips
                    top: MediaQuery.viewPaddingOf(context).top + headerH + 8,
                    bottom: MediaQuery.viewPaddingOf(context).bottom + 8,
                    child: AlphabetRail(letters: [for (final (l, _) in _letters) l], current: _atLetter, onLetter: _jumpToLetter),
                  ),
                if (picking)
                  Positioned(
                    left: 12,
                    right: 12,
                    bottom: 12 + MediaQuery.viewPaddingOf(context).bottom,
                    child: PickBar(count: _picked.length, onCompare: _picked.length >= compareMin ? _compare : null, onCancel: () => setState(() => _picked = const [])),
                  ),
              ]),
            ),
          );
        },
      );

  /// The filters, always in view: category, "works right now", auth, how much is free, HTTPS, CORS, sort - then the user's tags.
  Widget _filterBar(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return ListView(scrollDirection: Axis.horizontal, padding: const EdgeInsets.symmetric(horizontal: 12), children: [
      // its own Builder: the drawer belongs to the Scaffold below the outer builder's context
      Builder(builder: (ctx) => FilterPill(plainCategory(state.category), menu: true, selected: state.category != allCategory, onTap: () => Scaffold.of(ctx).openDrawer())),
      // one tap for "what can I try this minute": no key needed, or a demo key included
      FilterPill('Works right now', icon: Icons.bolt, selected: state.worksNow, onTap: () => state.setFilters(worksNow: !state.worksNow)),
      MenuPill(name: 'Auth', choices: authFilters, current: state.authFilter, onPick: (v) => state.setFilters(auth: v)),
      MenuPill(name: 'Free', choices: accessFilters, current: state.accessFilter, onPick: (v) => state.setFilters(access: v)),
      FilterPill('HTTPS', selected: state.httpsOnly, onTap: () => state.setFilters(https: !state.httpsOnly)),
      FilterPill('CORS', selected: state.corsOnly, onTap: () => state.setFilters(cors: !state.corsOnly)),
      MenuPill(name: 'Sort', choices: sortChoices, current: state.sort, onPick: state.setSort),
      if (_filtered)
        FilterPill('Clear', icon: Icons.close, onTap: () {
          // the search stays: only the choices in this row are cleared
          state.setFilters(auth: authFilters.first, access: accessFilters.first, https: false, cors: false, worksNow: false);
          state.setCategory(allCategory);
          state.setTagFilter(null);
        }),
      if (state.user.tagCounts.isNotEmpty) ...[
        Padding(padding: const EdgeInsets.fromLTRB(4, 10, 10, 10), child: VerticalDivider(width: 1, color: scheme.outlineVariant)),
        for (final (tag, count) in state.user.tagCounts)
          FilterPill('#$tag  $count', selected: state.tagFilter?.toLowerCase() == tag.toLowerCase(), onTap: () => state.setTagFilter(state.tagFilter?.toLowerCase() == tag.toLowerCase() ? null : tag)),
      ],
    ]);
  }
}
