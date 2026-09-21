// Request variables (the desktop app's RequestVariables): "{city}" in a test URL, header or body stands for the value
// saved under that name for the API. {key} is not one of them - that is the saved API key and has its own rules.
// A few names are always there: {today}, {yesterday}, {tomorrow} (yyyy-MM-dd), {now} (UTC, ISO 8601), {timestamp} (Unix seconds).

final _placeholderRx = RegExp(r'\{([A-Za-z_][A-Za-z0-9_-]{0,39})\}');

const builtInVariables = ['today', 'yesterday', 'tomorrow', 'now', 'timestamp'];

String _two(int n) => n.toString().padLeft(2, '0');
String _date(DateTime t) => '${t.year.toString().padLeft(4, '0')}-${_two(t.month)}-${_two(t.day)}';

String? _builtIn(String name, DateTime now) {
  switch (name.toLowerCase()) {
    case 'today':
      return _date(now);
    case 'yesterday':
      return _date(DateTime(now.year, now.month, now.day - 1));
    case 'tomorrow':
      return _date(DateTime(now.year, now.month, now.day + 1));
    case 'now':
      final u = now.toUtc();
      return '${_date(u)}T${_two(u.hour)}:${_two(u.minute)}:${_two(u.second)}Z';
    case 'timestamp':
      return (now.millisecondsSinceEpoch ~/ 1000).toString();
  }
  return null;
}

/// "name = value" per line; blank lines and lines without '=' are ignored. A later line with the same name (whatever the case) wins.
Map<String, String> parseVariables(String? text) {
  final map = <String, String>{};
  for (final raw in (text ?? '').split('\n')) {
    final line = raw.trim();
    final eq = line.indexOf('=');
    if (eq <= 0) continue;
    var name = line.substring(0, eq).trim();
    name = name.replaceAll(RegExp(r'^[{}]+|[{}]+$'), '');
    final whole = _placeholderRx.firstMatch('{$name}');
    if (whole == null || whole.group(0) != '{$name}' || name.toLowerCase() == 'key') continue;
    map.removeWhere((k, _) => k.toLowerCase() == name.toLowerCase());
    map[name] = line.substring(eq + 1).trim();
  }
  return map;
}

String variablesToText(Map<String, String> map) => map.entries.map((e) => '${e.key} = ${e.value}').join('\n');

/// The user's value, else a built-in one; null when the name is unknown (or is "key").
String? variableValue(String name, Map<String, String>? map, DateTime now) {
  if (name.toLowerCase() == 'key') return null;
  for (final e in (map ?? const <String, String>{}).entries) {
    if (e.key.toLowerCase() == name.toLowerCase() && e.value.isNotEmpty) return e.value;
  }
  return _builtIn(name, now);
}

/// [escape] is for a URL: the value is percent-encoded, so "New York" or "a&b" cannot break the address.
String fillVariables(String text, Map<String, String>? map, {bool escape = false, DateTime? now}) {
  final at = now ?? DateTime.now();
  return text.replaceAllMapped(_placeholderRx, (m) {
    final v = variableValue(m.group(1)!, map, at);
    return v == null ? m.group(0)! : (escape ? Uri.encodeComponent(v) : v);
  });
}

List<String> _distinct(Iterable<String> names) {
  final seen = <String>{};
  return [for (final n in names) if (seen.add(n.toLowerCase())) n];
}

/// Placeholders that nothing fills - what the user still has to give a value. {key} is never listed.
List<String> missingVariables(String text, Map<String, String>? map) {
  final now = DateTime.now();
  return _distinct(_placeholderRx.allMatches(text).map((m) => m.group(1)!).where((n) => n.toLowerCase() != 'key' && variableValue(n, map, now) == null));
}

/// Every placeholder in the text except {key} and the built-ins - the names worth a line in the Variables box.
List<String> variableNames(String text) =>
    _distinct(_placeholderRx.allMatches(text).map((m) => m.group(1)!).where((n) => n.toLowerCase() != 'key' && !builtInVariables.contains(n.toLowerCase())));
