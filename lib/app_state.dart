import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart' show rootBundle;
import 'package:path_provider/path_provider.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'knowledge.dart';
import 'models.dart';
import 'sources.dart';
import 'user_data.dart';
import 'vault.dart';

const allCategory = 'All APIs';
const favouritesCategory = '★ Favourites';
const demoKeyCategory = '🔑 Demo key included';
const myKeysCategory = '🔐 My keys saved';
const collectionPrefix = '📁 ';

const authFilters = ['Any auth', 'No key needed', 'Demo key included', 'Key optional or none', 'API key', 'OAuth', 'Unknown'];
const accessFilters = ['Any free access', 'Full free access', 'Free tier (limited)', 'Demo / trial only', 'Not stated'];

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
  ThemeModeSetting theme = ThemeModeSetting.system;

  /// Category name → how many of the filtered APIs it holds (special categories first).
  List<(String, int)> categories = [];

  Future<void> load() async {
    _knowledgeJson = await rootBundle.loadString('assets/knowledge.json');
    knowledge = Knowledge.parse(_knowledgeJson);
    _prefs = await SharedPreferences.getInstance();
    await _loadUser();
    try {
      keyed = await vault.names();
    } catch (ex) {
      // a keystore that will not open (seen after restoring a phone from a backup) must not stop the app
      vaultProblem = 'The phone\'s secure storage could not be opened ($ex). Keys saved now last until the app closes.';
      vault = MemoryKeyVault();
    }
    theme =ThemeModeSetting.values[(_prefs.getInt('theme') ?? 0).clamp(0, 2)];
    try {
      final file = await _catalogueFile();
      if (await file.exists()) {
        final cat = Catalogue.fromJson(jsonDecode(await file.readAsString()) as Map<String, dynamic>);
        _use(cat);
        status = 'Loaded ${all.length} APIs from the last scan (${_when(cat.scannedAt)}).';
      }
    } catch (_) {
      status = 'The saved catalogue could not be read - scan again.';
    }
    if (all.isEmpty && status.isEmpty) status = 'Press Scan to find free APIs.';
    ready = true;
    notifyListeners();
  }

  Future<File> _catalogueFile() async => File('${(await getApplicationSupportDirectory()).path}/catalogue.json');
  Future<File> _userFile() async => File('${(await getApplicationSupportDirectory()).path}/user.json');

  Future<void> _loadUser() async {
    try {
      final file = await _userFile();
      if (await file.exists()) {
        user = UserData.fromJson(jsonDecode(await file.readAsString()) as Map<String, dynamic>);
        return;
      }
    } catch (_) {
      // unreadable: start clean rather than not start
    }
    // version 1.0 kept the favourites in the settings
    user = UserData()..favourites = {...?_prefs.getStringList('favourites')};
  }

  Future<void> _pendingSave = Future.value();

  /// Writes go one after another, to a temporary file first: a save that is cut short leaves the old file whole.
  Future<void> saveUser() => _pendingSave = _pendingSave.then((_) async {
        try {
          final file = await _userFile();
          final temp = File('${file.path}.tmp');
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
    all = [for (final e in cat.entries) ApiView(e, knowledge)];
    _byKey = {for (final v in all) v.entry.key: v};
    _pickApiOfTheDay();
    applyFilter();
  }

  Map<String, ApiView> _byKey = {};
  ApiView? find(String key) => _byKey[key];

  /// One API a day that answers without any sign-up - the same pick as the desktop app makes from the same catalogue.
  void _pickApiOfTheDay() {
    final candidates = [for (final v in all) if (v.example != null && (v.keylessWorks || v.hasDemoKey)) v]..sort((a, b) => a.entry.key.compareTo(b.entry.key));
    final now = DateTime.now();
    final dayNumber = DateTime.utc(now.year, now.month, now.day).difference(DateTime.utc(1, 1, 1)).inDays; // .NET's DateOnly.DayNumber
    apiOfTheDay = candidates.isEmpty ? null : candidates[(dayNumber + _dayOffset) % candidates.length];
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
      final before = all.length;
      final known = {for (final v in all) v.entry.key};
      final cat = Catalogue(DateTime.now(), entries);
      await (await _catalogueFile()).writeAsString(jsonEncode(cat.toJson()), flush: true);
      _use(cat);
      final added = before == 0 ? 0 : entries.where((e) => !known.contains(e.key)).length;
      final failed = allNotes.where((n) => n.contains('failed')).toList();
      status = 'Scan finished: ${all.length} unique APIs in ${{for (final v in all) v.entry.category}.length} categories'
          '${before > 0 ? ' - $added new since the last scan' : ''}'
          '${failed.isEmpty ? '.' : '. ${failed.length} source(s) failed: ${failed.join('; ')}'}';
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
            _matchesAuth(v) &&
            (accessFilter == accessFilters.first || v.accessLabel == accessFilter) &&
            (tagFilter == null || tagsOf(v).any((t) => t.toLowerCase() == tagFilter!.toLowerCase())) &&
            words.every((w) => v.searchText.contains(w) || tagsOf(v).any((t) => t.toLowerCase().contains(w))))
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
    notifyListeners();
  }

  int specialCategoryCount = 3;

  void setSearch(String value) {
    search = value;
    applyFilter();
  }

  void setCategory(String value) {
    category = value;
    applyFilter();
  }

  void setFilters({String? auth, String? access, bool? https, bool? cors}) {
    authFilter = auth ?? authFilter;
    accessFilter = access ?? accessFilter;
    httpsOnly = https ?? httpsOnly;
    corsOnly = cors ?? corsOnly;
    applyFilter();
  }

  void clearFilters() {
    search = '';
    category = allCategory;
    tagFilter = null;
    authFilter = authFilters.first;
    accessFilter = accessFilters.first;
    httpsOnly = corsOnly = false;
    applyFilter();
  }

  int get activeFilterCount =>
      (authFilter != authFilters.first ? 1 : 0) + (accessFilter != accessFilters.first ? 1 : 0) + (httpsOnly ? 1 : 0) + (corsOnly ? 1 : 0);

  bool isFavourite(ApiView v) => favourites.contains(v.entry.key);

  void toggleFavourite(ApiView v) {
    if (!favourites.remove(v.entry.key)) favourites.add(v.entry.key);
    saveUser();
    applyFilter();
  }

  void setTagFilter(String? tag) {
    tagFilter = tag;
    applyFilter();
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
    final keys = wantKeys ? await compute(openKeysIsolate, {'json': json, 'passphrase': passphrase}) : <String, String>{};
    final summary = mergeUserData(user, file.data)..secretsSkipped = file.hasSecrets && !wantKeys;
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
    if (all.isNotEmpty) {
      final mentioned = {...file.data.favourites, ...file.data.tags.keys, ...file.data.notes.keys, for (final c in file.data.collections.values) ...c, ...keys.keys};
      summary.notInCatalogue = mentioned.where((k) => !_byKey.containsKey(k)).length;
    }
    await saveUser();
    applyFilter();
    return summary;
  }

  /// The same file format as the desktop app's export. Keys go in only with a passphrase.
  Future<String> exportBackup(String? passphrase) async {
    final withKeys = passphrase != null && passphrase.isNotEmpty;
    return compute(writeBackupIsolate, {'data': jsonDecode(jsonEncode(user.toJson())), 'keys': withKeys ? await vault.readAll() : <String, String>{}, 'passphrase': passphrase});
  }

  int get userItemCount => user.favourites.length + user.tags.length + user.notes.length + user.collections.length;

  void setTheme(ThemeModeSetting value) {
    theme = value;
    _prefs.setInt('theme', value.index);
    notifyListeners();
  }
}

enum ThemeModeSetting { system, light, dark }
