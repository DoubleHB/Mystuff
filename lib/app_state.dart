import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart' show rootBundle;
import 'package:path_provider/path_provider.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'knowledge.dart';
import 'models.dart';
import 'sources.dart';

const allCategory = 'All APIs';
const favouritesCategory = '★ Favourites';
const demoKeyCategory = '🔑 Demo key included';

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
  Set<String> favourites = {};

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
    favourites = {...?_prefs.getStringList('favourites')};
    theme = ThemeModeSetting.values[(_prefs.getInt('theme') ?? 0).clamp(0, 2)];
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

  static String _when(DateTime t) => '${t.day}/${t.month}/${t.year} ${t.hour.toString().padLeft(2, '0')}:${t.minute.toString().padLeft(2, '0')}';

  void _use(Catalogue cat) {
    scannedAt = cat.scannedAt;
    all = [for (final e in cat.entries) ApiView(e, knowledge)];
    applyFilter();
  }

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
      status = 'Scan finished: ${all.length} unique APIs in ${categories.length - 3} categories'
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
            words.every(v.searchText.contains))
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
      for (final n in names) (n, counts[n] ?? 0),
    ];
    if (!categories.any((c) => c.$1 == category)) category = allCategory;
    rows = switch (category) {
      allCategory => pre,
      favouritesCategory => [for (final v in pre) if (favourites.contains(v.entry.key)) v],
      demoKeyCategory => [for (final v in pre) if (v.hasDemoKey) v],
      _ => [for (final v in pre) if (v.entry.category == category) v],
    };
    notifyListeners();
  }

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
    _prefs.setStringList('favourites', favourites.toList());
    applyFilter();
  }

  void setTheme(ThemeModeSetting value) {
    theme = value;
    _prefs.setInt('theme', value.index);
    notifyListeners();
  }
}

enum ThemeModeSetting { system, light, dark }
