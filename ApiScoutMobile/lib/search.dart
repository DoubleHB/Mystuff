// Search that forgives a typo, and the list of searches worth remembering.

final _tokenRx = RegExp(r'[^\p{L}\p{N}]+', unicode: true);

/// The words of a text, lower-cased, for the typo check.
List<String> searchTokens(String text) => [for (final t in text.toLowerCase().split(_tokenRx)) if (t.length >= 3) t];

/// A search word matches when the text contains it - or, for a word of four letters or more, when some word of the
/// text is within one edit of it (two edits from eight letters up): "wether" finds weather, "cocktial" finds cocktail.
bool wordMatches(String text, List<String> tokens, String word) {
  if (text.contains(word)) return true;
  if (word.length < 4) return false;
  final maxEdits = word.length >= 8 ? 2 : 1;
  for (final t in tokens) {
    if (t.length < word.length - maxEdits) continue;
    // a longer word of the text counts by its start: "forcast" is one edit from the start of "forecasts"
    final head = t.length > word.length + maxEdits ? t.substring(0, word.length + maxEdits) : t;
    if (editDistance(head, word, maxEdits) <= maxEdits) return true;
  }
  return false;
}

// the three rows of the edit-distance table, kept between calls: the typo check runs thousands of times per keystroke
// and must not allocate on every one (one isolate at a time uses them, so sharing is safe)
var _row0 = List<int>.filled(64, 0), _row1 = List<int>.filled(64, 0), _row2 = List<int>.filled(64, 0);

/// Edits (insert, delete, replace, swap two neighbours) that turn [a] into [b]; gives up at [max] + 1.
int editDistance(String a, String b, int max) {
  final n = a.length, m = b.length;
  if ((n - m).abs() > max) return max + 1;
  if (_row0.length < m + 1) {
    _row0 = List<int>.filled(m + 1, 0);
    _row1 = List<int>.filled(m + 1, 0);
    _row2 = List<int>.filled(m + 1, 0);
  }
  var prev2 = _row0, prev = _row1, cur = _row2;
  for (var j = 0; j <= m; j++) {
    prev2[j] = 0;
    prev[j] = j;
  }
  for (var i = 1; i <= n; i++) {
    cur[0] = i;
    var rowMin = i;
    for (var j = 1; j <= m; j++) {
      final cost = a.codeUnitAt(i - 1) == b.codeUnitAt(j - 1) ? 0 : 1;
      var v = prev[j] + 1;
      if (cur[j - 1] + 1 < v) v = cur[j - 1] + 1;
      if (prev[j - 1] + cost < v) v = prev[j - 1] + cost;
      if (i > 1 && j > 1 && a.codeUnitAt(i - 1) == b.codeUnitAt(j - 2) && a.codeUnitAt(i - 2) == b.codeUnitAt(j - 1) && prev2[j - 2] + cost < v) v = prev2[j - 2] + cost;
      cur[j] = v;
      if (v < rowMin) rowMin = v;
    }
    if (rowMin > max) return max + 1;
    final t = prev2;
    prev2 = prev;
    prev = cur;
    cur = t;
  }
  return prev[m];
}

/// The line shown over an empty list: what was typed, the filters, or both.
String noMatchText(String search, {required bool filtered}) {
  final s = search.trim();
  if (s.isEmpty) return 'Nothing matches these filters';
  final shown = s.length > 30 ? '${s.substring(0, 30)}…' : s;
  return filtered ? 'Nothing matches "$shown" with these filters' : 'Nothing matches "$shown"';
}

/// The recent list with [term] at the front: no duplicates (whatever the case), at most [keep] entries.
List<String> rememberSearch_(List<String> recent, String term, {int keep = 8}) {
  term = term.trim();
  if (term.length < 2) return recent;
  return [term, for (final r in recent) if (r.toLowerCase() != term.toLowerCase()) r].take(keep).toList();
}
