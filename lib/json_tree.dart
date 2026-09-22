// A JSON document as a flat list of lines, so a response of thousands of nodes can be a plain scrolling list
// with nodes that fold.

class JsonLine {
  final int index;
  final int depth;
  /// The object key, the list position as text, or null for the root.
  final String? key;
  final bool inList;
  final Object? value;
  final bool container;
  final bool isList;
  /// Children of a container; 0 for a leaf.
  final int count;
  final int parent;
  /// Index of the last descendant (== [index] for a leaf): folding skips to [end] + 1.
  int end;

  JsonLine({required this.index, required this.depth, required this.key, required this.inList, required this.value, required this.container, required this.isList, required this.count, required this.parent}) : end = index;

  String get valueText => container ? (isList ? '[$count]' : '{$count}') : jsonValueText(value);
}

/// How a leaf shows: strings in quotes, the rest as JSON writes them.
String jsonValueText(Object? v) => v == null
    ? 'null'
    : v is String
        ? '"${v.replaceAll('\n', '\\n')}"'
        : '$v';

/// Every node in document order.
List<JsonLine> flattenJson(Object? root) {
  final out = <JsonLine>[];
  void walk(Object? v, int depth, String? key, bool inList, int parent) {
    final isMap = v is Map, isList = v is List;
    final line = JsonLine(index: out.length, depth: depth, key: key, inList: inList, value: v, container: isMap || isList, isList: isList, count: isMap ? v.length : isList ? v.length : 0, parent: parent);
    out.add(line);
    if (isMap) {
      for (final e in v.entries) {
        walk(e.value, depth + 1, '${e.key}', false, line.index);
      }
    } else if (isList) {
      for (var i = 0; i < v.length; i++) {
        walk(v[i], depth + 1, '$i', true, line.index);
      }
    }
    line.end = out.length - 1;
  }

  walk(root, 0, null, false, -1);
  return out;
}

bool jsonLineMatches(JsonLine l, String query) {
  if (query.isEmpty) return true;
  if (l.key != null && !l.inList && l.key!.toLowerCase().contains(query)) return true;
  return !l.container && jsonValueText(l.value).toLowerCase().contains(query);
}

/// The lines to show: folded containers hide their descendants; with a query, only matching lines and the
/// containers on the way to them (folding is ignored while searching).
List<JsonLine> visibleJsonLines(List<JsonLine> all, Set<int> collapsed, String query) {
  query = query.toLowerCase().trim();
  if (query.isEmpty) {
    final out = <JsonLine>[];
    var i = 0;
    while (i < all.length) {
      final l = all[i];
      out.add(l);
      i = l.container && collapsed.contains(l.index) ? l.end + 1 : i + 1;
    }
    return out;
  }
  final keep = <int>{};
  for (final l in all) {
    if (!jsonLineMatches(l, query)) continue;
    var p = l.index;
    while (p >= 0 && keep.add(p)) {
      p = all[p].parent;
    }
  }
  return [for (final l in all) if (keep.contains(l.index)) l];
}

/// Line numbers (0-based) of the raw text that contain the query.
List<int> matchingLines(List<String> lines, String query) {
  query = query.toLowerCase().trim();
  if (query.isEmpty) return const [];
  return [for (var i = 0; i < lines.length; i++) if (lines[i].toLowerCase().contains(query)) i];
}
