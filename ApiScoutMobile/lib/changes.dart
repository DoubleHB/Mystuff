import 'models.dart';

/// One API whose facts moved between two scans.
class ChangedApi {
  final String key, name, what;
  const ChangedApi(this.key, this.name, this.what);
  Map<String, dynamic> toJson() => {'key': key, 'name': name, 'what': what};
  factory ChangedApi.fromJson(Map<String, dynamic> j) => ChangedApi(j['key'] as String? ?? '', j['name'] as String? ?? '', j['what'] as String? ?? '');
}

/// What a scan changed against the one before: waits in changes.json until the user has seen it.
class CatalogueDiff {
  final DateTime at;
  final List<(String, String)> added;
  final List<(String, String)> gone;
  final List<ChangedApi> changed;
  bool seen;
  CatalogueDiff({required this.at, required this.added, required this.gone, required this.changed, this.seen = false});

  int get total => added.length + gone.length + changed.length;

  String get summary {
    final parts = [
      if (added.isNotEmpty) '${added.length} new',
      if (gone.isNotEmpty) '${gone.length} gone',
      if (changed.isNotEmpty) '${changed.length} changed',
    ];
    return parts.isEmpty ? 'Nothing changed' : parts.join(', ');
  }

  Map<String, dynamic> toJson() => {
        'at': at.toIso8601String(),
        'added': [for (final (k, n) in added) {'key': k, 'name': n}],
        'gone': [for (final (k, n) in gone) {'key': k, 'name': n}],
        'changed': [for (final c in changed) c.toJson()],
        'seen': seen,
      };

  factory CatalogueDiff.fromJson(Map<String, dynamic> j) {
    List<(String, String)> pairs(String name) => [
          for (final e in (j[name] as List? ?? [])) if (e is Map) ((e['key'] as String? ?? ''), (e['name'] as String? ?? ''))
        ];
    return CatalogueDiff(
      at: DateTime.tryParse(j['at'] as String? ?? '') ?? DateTime.now(),
      added: pairs('added'),
      gone: pairs('gone'),
      changed: [for (final e in (j['changed'] as List? ?? [])) if (e is Map) ChangedApi.fromJson(e.cast<String, dynamic>())],
      seen: j['seen'] == true,
    );
  }
}

/// New, gone, and changed (category or auth) between two catalogues, each list in name order.
CatalogueDiff diffCatalogues(List<ApiEntry> before, List<ApiEntry> after, {DateTime? at}) {
  final old = {for (final e in before) e.key: e};
  final now = {for (final e in after) e.key: e};
  final added = <(String, String)>[], changed = <ChangedApi>[];
  for (final e in after) {
    final was = old[e.key];
    if (was == null) {
      added.add((e.key, e.name));
      continue;
    }
    final what = [
      if (was.category != e.category) 'Category: ${was.category} → ${e.category}',
      if (was.auth != e.auth) 'Auth: ${was.auth.name} → ${e.auth.name}',
    ];
    if (what.isNotEmpty) changed.add(ChangedApi(e.key, e.name, what.join('; ')));
  }
  final gone = [for (final e in before) if (!now.containsKey(e.key)) (e.key, e.name)];
  int byName((String, String) a, (String, String) b) => a.$2.toLowerCase().compareTo(b.$2.toLowerCase());
  return CatalogueDiff(at: at ?? DateTime.now(), added: added..sort(byName), gone: gone..sort(byName), changed: changed..sort((a, b) => a.name.toLowerCase().compareTo(b.name.toLowerCase())));
}
