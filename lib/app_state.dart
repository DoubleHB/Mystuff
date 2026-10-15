import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart' show rootBundle;
import 'package:path_provider/path_provider.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'background.dart';
import 'changes.dart';
import 'docs_scanner.dart';
import 'knowledge.dart';
import 'models.dart';
import 'notify.dart';
import 'search.dart';
import 'sources.dart';
import 'user_data.dart';
import 'vault.dart';
import 'widgets.dart' show logosOn;

const allCategory = 'All APIs';
const favouritesCategory = '★ Favourites';
const demoKeyCategory = '🔑 Demo key included';
const myKeysCategory = '🔐 My keys saved';
const collectionPrefix = '📁 ';

const authFilters = ['Any auth', 'No key needed', 'Demo key included', 'Key optional or none', 'API key', 'OAuth', 'Unknown'];
const accessFilters = ['Any free access', 'Full free access', 'Free tier (limited)', 'Demo / trial only', 'Not stated'];
const sortChoices = ['Name A-Z', 'Health first'];

/// The list in the chosen order. Health comes from freepublicapis.com's daily checks: the best-scoring first, APIs with
/// no score after them in name order. Name order is how the catalogue is stored, so it costs nothing.
List<ApiView> sortRows(List<ApiView> rows, String sort) {
  if (sort != sortChoices[1]) return rows;
  return [...rows]..sort((a, b) {
      final ha = a.entry.health, hb = b.entry.health;
      if (ha != hb) {
        if (ha == null) return 1;
        if (hb == null) return -1;
        return hb.compareTo(ha);
      }
      return a.name.toLowerCase().compareTo(b.name.toLowerCase());
    });
}

/// Everything the screens show: the catalogue, the filters and the user's favourites.
class AppState extends ChangeNotifier {
  late Knowledge knowledge;
  late String _knowledgeJson;
  late SharedPreferences _prefs;

  List<ApiView> all = [];
  List<ApiView> rows = [];
  DateTime? scannedAt;
  UserData user = UserData();
  Set<String> get favourites => user.favourites;

  /// The user's own API keys: only the names are kept in memory, a value is read from the keystore when it is needed.
  KeyVault vault;
  Set<String> keyed = {};
  String? vaultProblem;

  ApiView? apiOfTheDay;
  int _dayOffset = 0;
  String? tagFilter;

  /// Searches that led somewhere, newest first (shown under the search box while it is empty).
  List<String> recentSearches = [];

  /// API of the day as a 9:00 notification; a rescan once a week on Wi-Fi while the app is closed.
  bool dailyNotice = false;
  bool weeklyRescan = false;

  /// What the last scan (in the app or in the background) changed, until the user has looked.
  CatalogueDiff? changes;

  AppState({KeyVault? vault}) : vault = vault ?? SecureKeyVault();

  bool ready = false;
  bool busy = false;
  double progress = 0;
  String status = '';

  String search = '';
  String category = allCategory;
  String authFilter = authFilters.first;
  String accessFilter = accessFilters.first;
  bool httpsOnly = false;
  bool corsOnly = false;

  /// The "Works right now" chip: only APIs that answer without a sign-up (no key, or a published demo key).
  bool worksNow = false;
  String sort = sortChoices.first;
  ThemeModeSetting theme = ThemeModeSetting.system;

  /// The theme on its own notifier: MaterialApp listens to this and to nothing else.
  final themeMode = ValueNotifier<ThemeModeSetting>(ThemeModeSetting.system);

  /// Provider logos from the icon service (host names leave the phone for them); off = coloured initials only.
  bool showLogos = true;

  /// No catalogue on this phone yet: the app scans by itself instead of showing an empty list.
  bool firstLaunch = false;

  /// Category name → how many of the filtered APIs it holds (special categories first).
  List<(String, int)> categories = [];

  Future<void> load() async {
    _knowledgeJson = await rootBundle.loadString('assets/knowledge.json');
    knowledge = Knowledge.parse(_knowledgeJson);
    _prefs = await SharedPreferences.getInstance();
    await _loadUser();
    await _loadDocsScans();
    try {
      keyed = await vault.names();
    } catch (ex) {
      // a keystore that will not open (seen after restoring a phone from a backup) must not stop the app
      vaultProblem = 'The phone\'s secure storage could not be opened ($ex). Keys saved now last until the app closes.';
      vault = MemoryKeyVault();
    }
    theme = ThemeModeSetting.values[(_prefs.getInt('theme') ?? 0).clamp(0, 2)];
    themeMode.value = theme;
    showLogos = _prefs.getBool('show-logos') ?? true;
    logosOn.value = showLogos;
    recentSearches = _prefs.getStringList('recent-searches') ?? [];
    dailyNotice = _prefs.getBool('daily-notice') ?? false;
    weeklyRescan = _prefs.getBool('weekly-rescan') ?? false;
    restoreFilters(_prefs.getString('filters'));
    await _loadChanges();
    try {
      final file = await _catalogueFile();
      firstLaunch = !await file.exists();
      if (!firstLaunch) {
        var cat = Catalogue.fromJson(jsonDecode(await file.readAsString()) as Map<String, dynamic>);
        if (cat.rules != rulesHash(_knowledgeJson) && cat.entries.isNotEmpty) {
          // an app update brought new rules: same entries, fresh categories, no scan - off the UI isolate
          final fresh = await compute(recategoriseIsolate, {'knowledge': _knowledgeJson, 'entries': [for (final e in cat.entries) e.toJson()]});
          cat = Catalogue(cat.scannedAt, [for (final e in fresh) ApiEntry.fromJson((e as Map).cast<String, dynamic>())], rules: rulesHash(_knowledgeJson));
          await _saveCatalogue(cat);
        }
        _use(cat);
        status = 'Loaded ${all.length} APIs from the last scan (${_when(cat.scannedAt)}).';
      }
    } catch (ex) {
      // a damaged catalogue is set aside, not overwritten in silence, and the app scans again by itself
      await _setAside(await _catalogueFile(), 'catalogue', ex);
      firstLaunch = true;
      status = 'The saved catalogue could not be read, so the app is scanning again.';
    }
    if (all.isEmpty && status.isEmpty) status = 'Press Scan to find free APIs.';
    // no catalogue: the remembered category would be a headline over nothing, so the list falls back to All APIs
    if (all.isEmpty) applyFilter();
    ready = true;
    notifyListeners();
    if (dailyNotice) await DailyNotice.schedule(dailyItems());
  }

  Future<File> _catalogueFile() async => File('${(await getApplicationSupportDirectory()).path}/catalogue.json');
  Future<File> _userFile() async => File('${(await getApplicationSupportDirectory()).path}/user.json');
  Future<File> _changesFile() async => File('${(await getApplicationSupportDirectory()).path}/changes.json');

  Future<void> _saveCatalogue(Catalogue cat) async => writeFileAtomically(await _catalogueFile(), jsonEncode(cat.toJson()));

  Future<void> _loadChanges() async {
    try {
      final file = await _changesFile();
      if (await file.exists()) changes = CatalogueDiff.fromJson(jsonDecode(await file.readAsString()) as Map<String, dynamic>);
    } catch (_) {
      changes = null;
    }
  }

  Future<void> _saveChanges() async {
    try {
      final file = await _changesFile();
      if (changes == null) {
        if (await file.exists()) await file.delete();
      } else {
        await writeFileAtomically(file, jsonEncode(changes!.toJson()));
      }
    } catch (_) {}
  }

  /// The strip above the list shows while this is true.
  bool get hasUnseenChanges => changes != null && !changes!.seen && changes!.total > 0;

  void markChangesSeen() {
    if (changes == null) return;
    changes!.seen = true;
    _saveChanges();
    notifyListeners();
  }

  /// A file this launch could not read, and what was done about it (a strip above the list until dismissed).
  String? dataProblem;

  void dismissDataProblem() {
    dataProblem = null;
    notifyListeners();
  }

  Future<void> _loadUser() async {
    try {
      final file = await _userFile();
      if (await file.exists()) {
        user = UserData.fromJson(jsonDecode(await file.readAsString()) as Map<String, dynamic>);
        return;
      }
    } catch (ex) {
      // unreadable: start clean rather than not start - but keep the file, and say so, instead of overwriting it
      // with an empty one on the next star tap
      await _setAside(await _userFile(), 'favourites, tags, notes and collections', ex);
    }
    // version 1.0 kept the favourites in the settings
    user = UserData()..favourites = {...?_prefs.getStringList('favourites')};
  }

  /// Renames a file the app could not read to `name.unreadable-time` so nothing overwrites it, and notes it.
  Future<void> _setAside(File file, String what, Object error) async {
    try {
      if (!await file.exists()) return;
      final stamp = DateTime.now().toIso8601String().replaceAll(':', '-').split('.').first;
      final aside = '${file.path}.unreadable-$stamp';
      await file.rename(aside);
      dataProblem = 'The file with your $what could not be read ($error). It was kept as ${aside.split('/').last}; the app starts without it.';
    } catch (_) {
      dataProblem = 'The file with your $what could not be read ($error).';
    }
  }

  Future<void> _pendingSave = Future.value();

  /// Writes go one after another, to a temporary file first: a save that is cut short leaves the old file whole.
  Future<void> saveUser() => _pendingSave = _pendingSave.then((_) async {
        try {
          final file = await _userFile();
          final temp = File('${file.path}.$pid.tmp');
          await temp.writeAsString(jsonEncode(user.toJson()), flush: true);
          await temp.rename(file.path);
        } catch (ex) {
          status = 'Could not save your changes: $ex';
          notifyListeners();
        }
      });

  static String _when(DateTime t) => '${t.day}/${t.month}/${t.year} ${t.hour.toString().padLeft(2, '0')}:${t.minute.toString().padLeft(2, '0')}';

  void _use(Catalogue cat) {
    scannedAt = cat.scannedAt;
    all = [for (final e in cat.entries) ApiView(e, knowledge)..docsAccess = accessFromDocs(_docsScans[e.key])];
    _byKey = {for (final v in all) v.entry.key: v};
    // a link or notification made before two listings were folded together still opens the API
    for (final v in all) {
      for (final a in v.entry.aliases) {
        _byKey.putIfAbsent(a, () => v);
      }
    }
    _adoptAliases();
    _pickApiOfTheDay();
    applyFilter();
  }

  /// For widget tests: a catalogue without a scan or a file.
  @visibleForTesting
  void useCatalogue(Catalogue cat) {
    _use(cat);
    notifyListeners();
  }

  Map<String, ApiView> _byKey = {};
  ApiView? find(String key) => _byKey[key];

  /// Favourites, tags, notes, collections, variables and keys saved under a key that a scan folded into another -
  /// and back again when a later scan lists the folded key on its own and the other has gone (user.renames remembers
  /// every move, so no star is stranded under a key that no row has).
  void _adoptAliases() {
    final forward = {for (final v in all) for (final a in v.entry.aliases) if (_byKey[a] == v) a: v.entry.key};
    final backward = <String, String>{};
    for (final e in user.renames.entries) {
      final from = e.key, to = e.value;
      final toRow = _byKey[to], fromRow = _byKey[from];
      // the row the data moved to is gone, the one it came from is back as its own row: move it back
      if (toRow == null && fromRow != null && fromRow.entry.key == from) backward[to] = from;
    }
    final map = {...backward, ...forward};
    if (map.isEmpty) return;
    var changed = adoptAliases(user, map);
    for (final e in forward.entries) {
      if (user.renames[e.key] != e.value) {
        user.renames[e.key] = e.value;
        changed = true;
      }
    }
    for (final e in backward.entries) {
      changed = user.renames.remove(e.value) != null || changed;
    }
    if (changed) saveUser();
    final moves = [for (final e in map.entries) if (keyed.contains(e.key)) e];
    if (moves.isNotEmpty) _moveKeys(moves);
  }

  Future<void> _moveKeys(List<MapEntry<String, String>> moves) async {
    if (vaultProblem != null) return; // a vault that forgets at close is no place to move keys through
    for (final m in moves) {
      try {
        // a key already saved under the surviving name wins; the other is left where it is, never deleted unread
        if (keyed.contains(m.value)) continue;
        final value = await vault.read(m.key) ?? '';
        if (value.isEmpty) continue;
        await vault.write(m.value, value);
        keyed.add(m.value);
        await vault.delete(m.key);
        keyed.remove(m.key);
      } catch (_) {
        // the keystore said no: the key stays where it was, under the old name
      }
    }
    applyFilter();
  }

  /// One API a day that answers without any sign-up - the same pick as the desktop app makes from the same catalogue.
  void _pickApiOfTheDay() => apiOfTheDay = apiOfTheDayFor(DateTime.now(), offset: _dayOffset);

  static int _dayNumber(DateTime day) => DateTime.utc(day.year, day.month, day.day).difference(DateTime.utc(1, 1, 1)).inDays; // .NET's DateOnly.DayNumber

  ApiView? apiOfTheDayFor(DateTime day, {int offset = 0}) {
    final candidates = [for (final v in all) if (v.example != null && (v.keylessWorks || v.hasDemoKey)) v]..sort((a, b) => a.entry.key.compareTo(b.entry.key));
    return candidates.isEmpty ? null : candidates[(_dayNumber(day) + offset) % candidates.length];
  }

  /// The next seven mornings' picks, for the notifications.
  List<DailyItem> dailyItems() {
    final today = DateTime.now();
    return [
      for (var d = 0; d < 7; d++)
        if (apiOfTheDayFor(today.add(Duration(days: d))) case final v?)
          (
            when: DateTime(today.year, today.month, today.day + d, 9),
            key: v.entry.key,
            title: 'API of the day: ${v.name}',
            body: v.entry.description.isEmpty ? '${v.hasDemoKey ? 'Demo key included' : 'No key needed'} · tap to try it' : v.entry.description,
          ),
    ];
  }

  Future<void> setDailyNotice(bool on) async {
    if (on && !await DailyNotice.askPermission()) {
      status = 'Notifications are off for API Free in Android\'s settings.';
      on = false;
    }
    dailyNotice = on;
    await _prefs.setBool('daily-notice', on);
    notifyListeners();
    if (on) {
      await DailyNotice.schedule(dailyItems());
    } else {
      await DailyNotice.cancelAll();
    }
  }

  Future<void> setWeeklyRescan(bool on) async {
    weeklyRescan = on;
    await _prefs.setBool('weekly-rescan', on);
    notifyListeners();
    try {
      if (on) {
        await enableWeeklyRescan();
      } else {
        await disableWeeklyRescan();
      }
    } catch (ex) {
      status = 'Could not set up the weekly rescan: $ex';
      weeklyRescan = false;
      await _prefs.setBool('weekly-rescan', false);
      notifyListeners();
    }
  }

  void anotherApiOfTheDay() {
    _dayOffset++;
    _pickApiOfTheDay();
    notifyListeners();
  }

  /// The card is for browsing: it steps aside as soon as the user is looking for something.
  bool get showApiOfTheDay => apiOfTheDay != null && search.isEmpty && category == allCategory && tagFilter == null && activeFilterCount == 0;

  /// Downloads every source (in parallel), then parses, merges and categorises in a background isolate.
  Future<void> scan() async {
    if (busy) return;
    busy = true;
    progress = 0;
    status = 'Contacting ${sources.length} sources…';
    notifyListeners();
    try {
      final texts = <String, String>{};
      final notes = <String>[];
      var done = 0;
      await Future.wait(sources.map((s) async {
        try {
          texts[s.id] = await fetchText(s.url);
        } catch (ex) {
          notes.add('${s.name}: failed - $ex');
        }
        done++;
        progress = done / (sources.length + 1);
        status = '${s.name} read ($done of ${sources.length})';
        notifyListeners();
      }));
      if (texts.isEmpty) {
        status = 'The scan found nothing - are you online? ${notes.join('; ')}';
        return;
      }
      status = 'Merging and categorising…';
      notifyListeners();
      final result = await compute(buildCatalogue, {'knowledge': _knowledgeJson, 'texts': texts, 'notes': notes});
      final entries = [for (final e in (result['entries'] as List)) ApiEntry.fromJson((e as Map).cast<String, dynamic>())];
      final allNotes = (result['notes'] as List).cast<String>();
      if (entries.isEmpty) {
        status = 'The scan found nothing. ${allNotes.join('; ')}';
        return;
      }
      if (all.isNotEmpty && (texts.length < 3 || entries.length < 500 || entries.length < all.length ~/ 2)) {
        // most sources down: a thin result must not replace a good catalogue (the background rescan has the same rule)
        status = 'Only ${texts.length} of ${sources.length} sources answered (${entries.length} APIs) - the catalogue of ${all.length} from ${scannedAt == null ? 'before' : _when(scannedAt!)} is kept. Try again later.';
        return;
      }
      final before = all.length;
      if (before > 0) {
        changes = diffCatalogues([for (final v in all) v.entry], entries);
        await _saveChanges();
      }
      final cat = Catalogue(DateTime.now(), entries, rules: rulesHash(_knowledgeJson));
      await _saveCatalogue(cat);
      _use(cat);
      final failed = allNotes.where((n) => n.contains('failed')).toList();
      status = 'Scan finished: ${all.length} unique APIs in ${{for (final v in all) v.entry.category}.length} categories'
          '${before > 0 ? ' - ${changes!.summary.toLowerCase()} since the last scan' : ''}'
          '${failed.isEmpty ? '.' : '. ${failed.length} source(s) failed: ${failed.join('; ')}'}';
      if (dailyNotice) await DailyNotice.schedule(dailyItems());
    } catch (ex) {
      status = 'Scan failed: $ex';
    } finally {
      busy = false;
      progress = 0;
      notifyListeners();
    }
  }

  // ---------------------------------------------------------------- filters

  bool _matchesAuth(ApiView v) => switch (authFilter) {
        'No key needed' => v.entry.auth == AuthKind.none,
        'Demo key included' => v.hasDemoKey,
        'Key optional or none' => v.keylessWorks || v.hasDemoKey,
        'API key' => v.entry.auth == AuthKind.apiKey,
        'OAuth' => v.entry.auth == AuthKind.oauth,
        'Unknown' => v.entry.auth == AuthKind.unknown || v.entry.auth == AuthKind.other,
        _ => true,
      };

  void applyFilter() {
    final words = search.toLowerCase().split(' ').where((w) => w.isNotEmpty).toList();
    final pre = [
      for (final v in all)
        if ((!httpsOnly || v.entry.https == true) &&
            (!corsOnly || v.entry.cors == 'Yes') &&
            (!worksNow || v.worksNow) &&
            _matchesAuth(v) &&
            (accessFilter == accessFilters.first || v.accessLabel == accessFilter) &&
            (tagFilter == null || tagsOf(v).any((t) => t.toLowerCase() == tagFilter!.toLowerCase())) &&
            words.every((w) => v.matchesWord(w) || tagsOf(v).any((t) => t.toLowerCase().contains(w))))
          v
    ];
    final counts = <String, int>{};
    for (final v in pre) {
      counts[v.entry.category] = (counts[v.entry.category] ?? 0) + 1;
    }
    final names = {for (final v in all) v.entry.category}.toList()
      ..sort((a, b) => a == 'Other' ? 1 : b == 'Other' ? -1 : a.toLowerCase().compareTo(b.toLowerCase()));
    categories = [
      (allCategory, pre.length),
      (favouritesCategory, pre.where((v) => favourites.contains(v.entry.key)).length),
      (demoKeyCategory, pre.where((v) => v.hasDemoKey).length),
      if (keyed.isNotEmpty) (myKeysCategory, pre.where((v) => keyed.contains(v.entry.key)).length),
      for (final c in user.collections.entries) ('$collectionPrefix${c.key}', pre.where((v) => c.value.contains(v.entry.key)).length),
      for (final n in names) (n, counts[n] ?? 0),
    ];
    specialCategoryCount = categories.length - names.length;
    if (!categories.any((c) => c.$1 == category)) category = allCategory;
    if (tagFilter != null && !user.tagCounts.any((t) => t.$1.toLowerCase() == tagFilter!.toLowerCase())) tagFilter = null;
    final collection = category.startsWith(collectionPrefix) ? user.collections[category.substring(collectionPrefix.length)] : null;
    rows = collection != null
        // a collection keeps the order it was put together in
        ? [for (final k in collection) if (_byKey[k] != null && pre.contains(_byKey[k])) _byKey[k]!]
        : switch (category) {
            allCategory => pre,
            favouritesCategory => [for (final v in pre) if (favourites.contains(v.entry.key)) v],
            demoKeyCategory => [for (final v in pre) if (v.hasDemoKey) v],
            myKeysCategory => [for (final v in pre) if (keyed.contains(v.entry.key)) v],
            _ => [for (final v in pre) if (v.entry.category == category) v],
          };
    if (collection == null) rows = sortRows(rows, sort);
    notifyListeners();
  }

  void setSort(String value) {
    sort = value;
    listVersion++;
    applyFilter();
    _saveFilters();
  }

  int specialCategoryCount = 3;

  /// Bumped whenever the user changes what the list shows (category, search, tag, filters) - the list scrolls back
  /// to the top on every change of it. Not bumped by favourites, tags, notes or scans, which must keep the scroll position.
  int listVersion = 0;

  void setSearch(String value) {
    search = value;
    listVersion++;
    applyFilter();
  }

  /// Called when a search led somewhere (the user pressed search, or opened a result).
  void rememberSearch(String term) {
    final next = rememberSearch_(recentSearches, term);
    if (next.join('\n') == recentSearches.join('\n')) return;
    recentSearches = next;
    _prefs.setStringList('recent-searches', recentSearches);
    notifyListeners();
  }

  void clearRecentSearches() {
    recentSearches = [];
    _prefs.setStringList('recent-searches', recentSearches);
    notifyListeners();
  }

  void forgetSearch(String term) {
    recentSearches = [for (final r in recentSearches) if (r != term) r];
    _prefs.setStringList('recent-searches', recentSearches);
    notifyListeners();
  }

  void setCategory(String value) {
    category = value;
    listVersion++;
    applyFilter();
    _saveFilters();
  }

  void setFilters({String? auth, String? access, bool? https, bool? cors, bool? worksNow}) {
    authFilter = auth ?? authFilter;
    accessFilter = access ?? accessFilter;
    httpsOnly = https ?? httpsOnly;
    corsOnly = cors ?? corsOnly;
    this.worksNow = worksNow ?? this.worksNow;
    listVersion++;
    applyFilter();
    _saveFilters();
  }

  void clearFilters() {
    search = '';
    category = allCategory;
    tagFilter = null;
    authFilter = authFilters.first;
    accessFilter = accessFilters.first;
    httpsOnly = corsOnly = worksNow = false;
    listVersion++;
    applyFilter();
    _saveFilters();
  }

  int get activeFilterCount =>
      (authFilter != authFilters.first ? 1 : 0) + (accessFilter != accessFilters.first ? 1 : 0) + (httpsOnly ? 1 : 0) + (corsOnly ? 1 : 0) + (worksNow ? 1 : 0);

  // ---------------------------------------------------------------- the filters come back next launch

  /// The category, chips and tag as one JSON line (the search box is not kept: it is for the moment).
  String encodeFilters() => jsonEncode({
        'category': category,
        'auth': authFilter,
        'access': accessFilter,
        'https': httpsOnly,
        'cors': corsOnly,
        'worksNow': worksNow,
        'sort': sort,
        if (tagFilter != null) 'tag': tagFilter,
      });

  /// Takes back what [encodeFilters] wrote. Anything missing or unknown keeps its default, and a category or tag that
  /// is no longer there is dropped by the next [applyFilter].
  void restoreFilters(String? json) {
    if (json == null || json.isEmpty) return;
    try {
      final j = jsonDecode(json);
      if (j is! Map) return;
      if (j['category'] is String && (j['category'] as String).isNotEmpty) category = j['category'] as String;
      if (authFilters.contains(j['auth'])) authFilter = j['auth'] as String;
      if (accessFilters.contains(j['access'])) accessFilter = j['access'] as String;
      httpsOnly = j['https'] == true;
      corsOnly = j['cors'] == true;
      worksNow = j['worksNow'] == true;
      if (sortChoices.contains(j['sort'])) sort = j['sort'] as String;
      tagFilter = j['tag'] is String && (j['tag'] as String).isNotEmpty ? j['tag'] as String : null;
    } catch (_) {
      // a settings line that does not parse is no settings line
    }
  }

  void _saveFilters() => _prefs.setString('filters', encodeFilters());

  bool isFavourite(ApiView v) => favourites.contains(v.entry.key);

  void toggleFavourite(ApiView v) {
    if (!favourites.remove(v.entry.key)) favourites.add(v.entry.key);
    saveUser();
    applyFilter();
  }

  void setTagFilter(String? tag) {
    tagFilter = tag;
    listVersion++;
    applyFilter();
    _saveFilters();
  }

  // ---------------------------------------------------------------- tags, notes, collections

  List<String> tagsOf(ApiView v) => user.tags[v.entry.key] ?? const [];
  String noteOf(ApiView v) => user.notes[v.entry.key] ?? '';

  void setTags(ApiView v, List<String> tags) {
    tags.isEmpty ? user.tags.remove(v.entry.key) : user.tags[v.entry.key] = tags;
    saveUser();
    applyFilter();
  }

  void setNote(ApiView v, String note) {
    note.trim().isEmpty ? user.notes.remove(v.entry.key) : user.notes[v.entry.key] = note.trim();
    saveUser();
    notifyListeners();
  }

  List<String> collectionsOf(ApiView v) => [for (final c in user.collections.entries) if (c.value.contains(v.entry.key)) c.key];

  /// Same name (whatever the case) = same collection. Returns the name it ended up under.
  String addToCollection(ApiView v, String name) {
    name = name.trim();
    final existing = user.collections.keys.where((k) => k.toLowerCase() == name.toLowerCase());
    final list = user.collections.putIfAbsent(existing.isEmpty ? name : existing.first, () => []);
    if (!list.contains(v.entry.key)) list.add(v.entry.key);
    saveUser();
    applyFilter();
    return existing.isEmpty ? name : existing.first;
  }

  void removeFromCollection(ApiView v, String name) {
    user.collections[name]?.remove(v.entry.key);
    saveUser();
    applyFilter();
  }

  void deleteCollection(String name) {
    user.collections.remove(name);
    saveUser();
    applyFilter();
  }

  // ---------------------------------------------------------------- my keys (Android keystore)

  bool hasKey(ApiView v) => keyed.contains(v.entry.key);
  Future<String> keyOf(ApiView v) async => hasKey(v) ? (await vault.read(v.entry.key) ?? '') : '';

  /// Throws when the keystore refuses: the caller tells the user, and [keyed] is left as it was.
  Future<void> setKey(ApiView v, String value) async {
    value = value.trim();
    if (value.isEmpty) {
      await vault.delete(v.entry.key);
      keyed.remove(v.entry.key);
    } else {
      await vault.write(v.entry.key, value);
      keyed.add(v.entry.key);
    }
    applyFilter();
  }

  // ---------------------------------------------------------------- moving data between the desktop app and the phone

  /// Reads a file made by the desktop app's (or this app's) export and merges it in. A wrong passphrase changes nothing.
  Future<ImportSummary> importBackup(String json, String? passphrase) async {
    final file = BackupFile.read(json);
    final wantKeys = file.hasSecrets && passphrase != null && passphrase.isNotEmpty;
    final secrets = wantKeys ? await compute(openSecretsIsolate, {'json': json, 'passphrase': passphrase}) : const <String, dynamic>{};
    final keys = ((secrets['keys'] as Map?) ?? const {}).cast<String, String>();
    final requests = testRequestsFromJson(secrets['requests']);
    final summary = mergeUserData(user, file.data)..secretsSkipped = file.hasSecrets && !wantKeys;
    summary.variables = mergeVariables(user, {
      for (final e in ((secrets['variables'] as Map?) ?? const {}).entries) e.key as String: (e.value as Map).cast<String, String>(),
    });
    for (final e in keys.entries) {
      final local = await vault.read(e.key) ?? '';
      if (local.isNotEmpty) {
        if (local != e.value) summary.keptLocal++;
      } else {
        await vault.write(e.key, e.value);
        keyed.add(e.key);
        summary.keys++;
      }
    }
    if (requests.isNotEmpty) {
      // a request already saved on this phone stays (the desktop's rule)
      await ensureHistory();
      for (final e in requests.entries) {
        if (_requests!.containsKey(e.key)) {
          summary.keptLocal++;
        } else {
          _requests![e.key] = e.value;
          summary.requests++;
        }
      }
      if (summary.requests > 0) await _saveRequests();
    }
    if (all.isNotEmpty) {
      final mentioned = {...file.data.favourites, ...file.data.tags.keys, ...file.data.notes.keys, for (final c in file.data.collections.values) ...c, ...keys.keys, ...requests.keys};
      summary.notInCatalogue = mentioned.where((k) => !_byKey.containsKey(k)).length;
    }
    await saveUser();
    applyFilter();
    return summary;
  }

  /// The same file format as the desktop app's export. Keys and saved requests go in only with a passphrase.
  Future<String> exportBackup(String? passphrase) async {
    final withKeys = passphrase != null && passphrase.isNotEmpty;
    if (withKeys) await ensureHistory();
    return compute(writeBackupIsolate, {
      'data': jsonDecode(jsonEncode(user.toJson())),
      'keys': withKeys ? await vault.readAll() : <String, String>{},
      'requests': withKeys ? {for (final e in _requests!.entries) e.key: e.value.toJson()} : <String, dynamic>{},
      'passphrase': passphrase,
    });
  }

  int get userItemCount => user.favourites.length + user.tags.length + user.notes.length + user.collections.length + user.variables.length;

  /// Keys, request variables and saved test requests only leave the phone inside the encrypted part of an export.
  int get secretItemCount => keyed.length + user.variables.length + (_requests?.length ?? 0);

  // ---------------------------------------------------------------- saved test requests (sealed like the history)

  Map<String, TestRequest>? _requests;

  Future<File> _requestsFile() async => File('${(await getApplicationSupportDirectory()).path}/requests.bin');

  /// The request last tested for this API when it differed from the suggested one; null = the suggested one.
  TestRequest? savedRequestOf(ApiView v) => _requests?[v.entry.key];

  /// Remembers [r] for the API, or forgets the saved one with null. Nothing is written when nothing changed.
  Future<void> setSavedRequest(ApiView v, TestRequest? r) async {
    await ensureHistory();
    final have = _requests![v.entry.key];
    if (r == null ? have == null : have != null && have.sameAs(r)) return;
    r == null ? _requests!.remove(v.entry.key) : _requests![v.entry.key] = r;
    await _saveRequests();
  }

  Future<void> _saveRequests() => _writeSealed(_requestsFile, jsonEncode({for (final e in _requests!.entries) e.key: e.value.toJson()}));

  // ---------------------------------------------------------------- request variables

  Map<String, String> variablesOf(ApiView v) => user.variables[v.entry.key] ?? const {};

  void setVariables(ApiView v, Map<String, String> map) {
    map.isEmpty ? user.variables.remove(v.entry.key) : user.variables[v.entry.key] = map;
    saveUser();
  }

  // ---------------------------------------------------------------- request history (sealed with a key from the keystore)

  Map<String, List<TestHistoryEntry>>? _history;
  Uint8List? _historyKey;

  Future<File> _historyFile() async => File('${(await getApplicationSupportDirectory()).path}/history.bin');

  /// Read on first use, not at start-up: most visits never open it. The saved test requests share the key and come with it.
  /// One load at a time: a second caller waits for the first instead of seeing empty maps that a write could then persist.
  Future<void> ensureHistory() => _historyLoading ??= _loadHistory();
  Future<void>? _historyLoading;

  /// Set when sealed files exist that could not be opened (no seal key, a vault that would not open, or a key that does not
  /// fit): nothing is written to them this session, so a healthy next launch still finds them whole.
  String? historyProblem;

  Future<void> _loadHistory() async {
    final history = <String, List<TestHistoryEntry>>{};
    final requests = <String, TestRequest>{};
    try {
      final saved = vaultProblem == null ? await vault.readSecret('history-key') : null;
      if (saved != null) _historyKey = base64Decode(saved);
      final file = await _historyFile(), requestsFile = await _requestsFile();
      final sealedExists = await file.exists() || await requestsFile.exists();
      if (_historyKey == null) {
        if (sealedExists) historyProblem = 'The saved test requests and history could not be unlocked this time. Nothing new is saved until they can be.';
      } else {
        var opened = true;
        if (await file.exists()) {
          final plain = await compute(openIsolate, [_historyKey!, await file.readAsBytes()]);
          if (plain != null) {
            history.addAll(historyFromJson(jsonDecode(utf8.decode(plain))));
          } else {
            opened = false;
          }
        }
        if (await requestsFile.exists()) {
          final plain = await compute(openIsolate, [_historyKey!, await requestsFile.readAsBytes()]);
          if (plain != null) {
            requests.addAll(testRequestsFromJson(jsonDecode(utf8.decode(plain))));
          } else {
            opened = false;
          }
        }
        if (!opened) historyProblem = 'The saved test requests and history on this phone do not fit the key in its secure storage, so they are left as they are. Nothing new is saved to them.';
      }
    } catch (_) {
      historyProblem ??= 'The saved test requests and history could not be read this time. Nothing new is saved until they can be.';
    }
    _history = history;
    _requests = requests;
  }

  List<TestHistoryEntry> historyOf(ApiView v) => _history?[v.entry.key] ?? const [];

  /// How many APIs keep a history: beyond this the one not tested for longest is dropped, so the sealed file stays a
  /// few megabytes at most however many APIs have been tried over the years.
  static const historyApis = 100;

  Future<void> addHistory(ApiView v, TestHistoryEntry entry) async {
    await ensureHistory();
    final list = _history!.putIfAbsent(v.entry.key, () => []);
    list.insert(0, entry);
    if (list.length > historyPerApi) list.removeRange(historyPerApi, list.length);
    while (_history!.length > historyApis) {
      final oldest = _history!.entries.reduce((a, b) => _newest(a.value).isBefore(_newest(b.value)) ? a : b);
      _history!.remove(oldest.key);
    }
    await _saveHistory();
  }

  static DateTime _newest(List<TestHistoryEntry> list) => list.isEmpty ? DateTime.fromMillisecondsSinceEpoch(0) : list.map((h) => h.at).reduce((a, b) => a.isAfter(b) ? a : b);

  Future<void> clearHistory(ApiView v) async {
    await ensureHistory();
    if (_history!.remove(v.entry.key) != null) await _saveHistory();
  }

  Future<void> _saveHistory() => _writeSealed(_historyFile, jsonEncode({for (final e in _history!.entries) e.key: [for (final h in e.value) h.toJson()]}));

  /// Seals [json] with the keystore-held key (made on first use) and writes it whole, one write after another.
  Future<void> _writeSealed(Future<File> Function() which, String json) => _pendingSave = _pendingSave.then((_) async {
        // never mint a new seal key over files sealed with one this session could not read, nor into a vault that
        // forgets at close: either would make the real files unreadable for good
        if (historyProblem != null || (_historyKey == null && vaultProblem != null)) return;
        try {
          if (_historyKey == null) {
            _historyKey = newSealKey();
            await vault.writeSecret('history-key', base64Encode(_historyKey!));
          }
          final file = await which();
          final temp = File('${file.path}.$pid.tmp');
          final sealed = await compute(sealIsolate, [_historyKey!, Uint8List.fromList(utf8.encode(json))]);
          await temp.writeAsBytes(sealed, flush: true);
          await temp.rename(file.path);
        } catch (_) {
          // the history is a convenience: failing to save it must not get in the way of the test itself
        }
      });

  // ---------------------------------------------------------------- docs scans (public information: kept as plain JSON)

  Map<String, DocsScanResult> _docsScans = {};
  final scanningDocs = <String>{};

  Future<File> _docsFile() async => File('${(await getApplicationSupportDirectory()).path}/docscans.json');

  Future<void> _loadDocsScans() async {
    try {
      final file = await _docsFile();
      if (!await file.exists()) return;
      final j = jsonDecode(await file.readAsString());
      if (j is Map) _docsScans = {for (final e in j.entries) if (e.value is Map) e.key as String: DocsScanResult.fromJson((e.value as Map).cast<String, dynamic>())};
    } catch (_) {
      _docsScans = {};
    }
  }

  DocsScanResult? docsScanOf(ApiView v) => _docsScans[v.entry.key];
  bool isScanningDocs(ApiView v) => scanningDocs.contains(v.entry.key);

  Future<void> scanDocsFor(ApiView v) async {
    if (!scanningDocs.add(v.entry.key)) return;
    notifyListeners();
    try {
      final result = await scanDocs(v.entry, exampleUrl: v.example);
      _docsScans[v.entry.key] = result;
      v.docsAccess = accessFromDocs(result);
      // only the latest few hundred are worth keeping on a phone
      if (_docsScans.length > 300) {
        final oldest = _docsScans.entries.toList()..sort((a, b) => a.value.scannedAt.compareTo(b.value.scannedAt));
        for (final e in oldest.take(_docsScans.length - 300)) {
          _docsScans.remove(e.key);
        }
      }
      _pendingSave = _pendingSave.then((_) async {
        try {
          final file = await _docsFile();
          final temp = File('${file.path}.$pid.tmp');
          await temp.writeAsString(jsonEncode({for (final e in _docsScans.entries) e.key: e.value.toJson()}), flush: true);
          await temp.rename(file.path);
        } catch (_) {}
      });
    } finally {
      scanningDocs.remove(v.entry.key);
      applyFilter(); // the free-access label (and so the filters and counts) may have changed
    }
  }

  void setTheme(ThemeModeSetting value) {
    theme = value;
    themeMode.value = value;
    _prefs.setInt('theme', value.index);
    notifyListeners();
  }

  void setShowLogos(bool on) {
    showLogos = on;
    logosOn.value = on;
    _prefs.setBool('show-logos', on);
    notifyListeners();
  }
}

enum ThemeModeSetting { system, light, dark }
