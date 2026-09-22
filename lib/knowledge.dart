import 'dart:convert';

import 'models.dart';
import 'search.dart';

/// FNV-1a over the knowledge file: the cached catalogue remembers which rules made its categories.
String rulesHash(String knowledgeJson) {
  var h = 0x811C9DC5;
  for (final c in knowledgeJson.codeUnits) {
    h = ((h ^ c) * 0x01000193) & 0xFFFFFFFF;
  }
  return h.toRadixString(16).padLeft(8, '0');
}

/// The rules the desktop app exports (assets/knowledge.json): category patterns, keyword patterns,
/// provider key hints. Loading them from that file keeps both apps in step - nothing here is a copy made by hand.
class Knowledge {
  final List<(RegExp, String)> categoryRules;
  final List<(RegExp, String)> keywordRules;
  final List<KeyHint> hints;
  final Set<String> fullFreeHosts;
  final Set<String> trialOnlyHosts;
  final String exportedFrom;
  late final Set<String> canonical = {for (final r in categoryRules) r.$2};

  Knowledge._(this.categoryRules, this.keywordRules, this.hints, this.fullFreeHosts, this.trialOnlyHosts, this.exportedFrom);

  factory Knowledge.parse(String json) {
    final j = jsonDecode(json) as Map<String, dynamic>;
    List<(RegExp, String)> rules(String name) => [
          for (final r in (j[name] as List))
            // .NET patterns; the constructs used (lookaround, \p{L}, \w) mean the same in Dart's unicode mode
            (RegExp(r['pattern'] as String, caseSensitive: false, unicode: true), r['category'] as String)
        ];
    return Knowledge._(
      rules('categoryRules'),
      rules('keywordRules'),
      [for (final h in (j['hints'] as List)) KeyHint.fromJson(h as Map<String, dynamic>)],
      {...(j['fullFreeHosts'] as List).cast<String>()},
      {...(j['trialOnlyHosts'] as List).cast<String>()},
      j['exportedFrom'] as String? ?? '',
    );
  }

  // ---- categories (Categoriser.Apply / FoldSmall)

  void categorise(ApiEntry e) {
    var raw = e.rawCategory.trim();
    if (raw.isNotEmpty) {
      for (final (rx, cat) in categoryRules) {
        if (rx.hasMatch(raw)) {
          if (cat != 'Other') {
            e.category = cat;
            return;
          }
          raw = ''; // "Miscellaneous" says nothing - let the description decide
          break;
        }
      }
    }
    final text = '${e.name} ${e.description}';
    var best = '';
    var bestScore = 0;
    for (final (rx, cat) in keywordRules) {
      final score = rx.allMatches(text).length;
      if (score > bestScore) {
        best = cat;
        bestScore = score;
      }
    }
    if (bestScore > 0) {
      e.category = best;
      return;
    }
    e.category = raw.isNotEmpty && raw.length <= 40 ? _titleCase(raw.replaceAll('_', ' ')) : 'Other';
  }

  /// Source categories that only hold a handful of APIs are folded into the canonical set.
  void foldSmall(List<ApiEntry> entries, {int minimum = 8}) {
    final counts = <String, int>{};
    for (final e in entries) {
      counts[e.category] = (counts[e.category] ?? 0) + 1;
    }
    for (final e in entries) {
      if (canonical.contains(e.category) || (counts[e.category] ?? 0) >= minimum) continue;
      final raw = e.rawCategory;
      e.rawCategory = '';
      categorise(e);
      e.rawCategory = raw;
    }
  }

  static String _titleCase(String s) =>
      s.toLowerCase().split(' ').map((w) => w.isEmpty ? w : w[0].toUpperCase() + w.substring(1)).join(' ');

  // ---- keys (KeyKnowledge.Find / AccessFor / GenericHowTo)

  KeyHint? findHint(ApiEntry e) {
    final host = Uri.tryParse(e.url)?.host.toLowerCase() ?? '';
    if (host.isEmpty) return null;
    for (final hint in hints) {
      for (final h in hint.hosts) {
        if (host == h || host.endsWith('.$h')) return hint;
      }
    }
    return null;
  }

  AccessLevel accessForHint(KeyHint hint) => fullFreeHosts.contains(hint.hosts.first)
      ? AccessLevel.fullFree
      : trialOnlyHosts.contains(hint.hosts.first)
          ? AccessLevel.trialOnly
          : AccessLevel.freeTier;

  static String genericHowTo(ApiEntry e) {
    switch (e.auth) {
      case AuthKind.none:
        return 'No key needed - this API is listed as open. Call its endpoints directly.\n\n'
            'Tip: send a descriptive User-Agent header and stay within any rate limit in the docs.';
      case AuthKind.apiKey:
        if (e.authRaw.toLowerCase().contains('mashape')) {
          return 'This API is served through RapidAPI.\n\n1. Create a free RapidAPI account.\n2. Open the API\'s page and subscribe to its free (Basic) plan.\n'
              '3. Copy your key from the code snippet panel.\n4. Send it as the X-RapidAPI-Key header, with X-RapidAPI-Host.';
        }
        return 'This API needs an API key. The usual route:\n\n'
            '1. Open the docs and look for \'Sign up\', \'Get API key\', \'Dashboard\' or \'Pricing\'.\n'
            '2. Create a free account - pick the Free / Developer / Hobby plan.\n'
            '3. Copy the key from your dashboard (sometimes it arrives by email).\n'
            '4. Send it the way the docs say - normally a query parameter (?api_key=…) or a header (X-Api-Key / Authorization: Bearer …).';
      case AuthKind.oauth:
        return 'This API uses OAuth.\n\n'
            '1. Open the docs and find the developer portal / \'Create an app\' page.\n'
            '2. Register an application to get a client id and client secret.\n'
            '3. Server-to-server data: use the client-credentials flow to swap id + secret for a bearer token.\n'
            '   Acting for a user: use the authorization-code flow with a redirect URL.\n'
            '4. Send the token as Authorization: Bearer <token>; refresh it when it expires.';
      case AuthKind.other:
        return 'The directory lists authentication as \'${e.authRaw}\'.\n\n'
            '${e.authRaw.toLowerCase().contains('agent') ? 'No key: just send a descriptive User-Agent header (app name + contact) with every request.' : 'Check the docs for what to send.'}';
      case AuthKind.unknown:
        return 'The directory that listed this API does not say whether a key is needed. Open the docs and look for '
            '\'Authentication\', \'API key\' or \'Getting started\'.';
    }
  }
}

/// One API as the UI shows it: the scanned entry plus what the knowledge says about its key (the desktop app's ApiRow).
class ApiView {
  final ApiEntry entry;
  final KeyHint? hint;
  final AccessLevel _baseAccess;

  /// What a docs scan worked out; only used when the directories and the key knowledge say nothing.
  AccessLevel docsAccess = AccessLevel.unknown;
  AccessLevel get access => _baseAccess == AccessLevel.unknown ? docsAccess : _baseAccess;
  late final String searchText = '${entry.name} ${entry.description} ${entry.category} ${entry.rawCategory} ${entry.url}'.toLowerCase();
  late final List<String> _searchTokens = searchTokens(searchText);

  /// The search word is in the text, or one typo away from a word of it.
  bool matchesWord(String word) => wordMatches(searchText, _searchTokens, word);

  ApiView(this.entry, Knowledge k)
      : hint = k.findHint(entry),
        _baseAccess = _access(entry, k);

  static final _freeTierWords = RegExp(r'\b(free (tier|plan|account|quota|usage)|freemium|free for (non-?commercial|personal)|limited free)\b', caseSensitive: false);
  static final _paidWords = RegExp(r'\b(free trial|\d+[- ]day trial|trial (version|period|account|key|plan)|paid|premium|subscription)\b', caseSensitive: false);

  static AccessLevel _access(ApiEntry e, Knowledge k) {
    final hint = k.findHint(e);
    if (hint != null) return k.accessForHint(hint);
    if (e.pricing == 'paid') return AccessLevel.trialOnly;
    if (e.pricing == 'open' || e.auth == AuthKind.none) return AccessLevel.fullFree;
    if (_freeTierWords.hasMatch(e.description)) return AccessLevel.freeTier;
    if (_paidWords.hasMatch(e.description)) return AccessLevel.trialOnly;
    return AccessLevel.unknown;
  }

  String get name => entry.name;
  String get url => entry.url;
  String? get demoKey => hint?.demoKey;
  bool get hasDemoKey => hint?.demoKey != null;
  bool get keylessWorks => (hint?.keylessWorks ?? false) || entry.auth == AuthKind.none;
  String? get example => hint?.example;
  String get host => Uri.tryParse(entry.url)?.host.replaceFirst(RegExp(r'^www\.'), '') ?? '';

  String get authLabel => switch (entry.auth) {
        AuthKind.none => 'No key',
        AuthKind.apiKey => 'API key',
        AuthKind.oauth => 'OAuth',
        AuthKind.other => entry.authRaw,
        AuthKind.unknown => 'Unknown',
      };

  /// Short badge: what it takes to start calling this API.
  String get keyBadge => hasDemoKey
      ? 'Demo key'
      : entry.auth == AuthKind.none
          ? 'Open'
          : (hint?.keylessWorks ?? false)
              ? 'Key optional'
              : entry.auth == AuthKind.apiKey
                  ? (hint?.signupUrl != null ? 'Free key' : 'Key needed')
                  : entry.auth == AuthKind.oauth
                      ? 'OAuth'
                      : entry.auth == AuthKind.other
                          ? 'Other'
                          : '?';

  String get keyHeadline => hasDemoKey
      ? 'A public demo key is available'
      : entry.auth == AuthKind.none
          ? 'No key needed'
          : (hint?.keylessWorks ?? false)
              ? 'Works without a key - a free key lifts the limits'
              : entry.auth == AuthKind.apiKey
                  ? 'Needs a free API key'
                  : entry.auth == AuthKind.oauth
                      ? 'Needs an OAuth app registration'
                      : entry.auth == AuthKind.other
                          ? 'Auth: ${entry.authRaw}'
                          : 'Key requirements not stated';

  String get howTo => hint?.howTo ?? Knowledge.genericHowTo(entry);

  String get accessLabel => switch (access) {
        AccessLevel.fullFree => 'Full free access',
        AccessLevel.freeTier => 'Free tier (limited)',
        AccessLevel.trialOnly => 'Demo / trial only',
        AccessLevel.unknown => 'Not stated',
      };

  String get accessNote => switch (access) {
        AccessLevel.fullFree => hint != null
            ? 'The whole API is free - a key (if any) is only for fair-use limits.'
            : entry.pricing == 'open'
                ? 'Flagged as open source / open access by the directory that listed it.'
                : 'Listed as needing no key, so everything it offers is open to call.',
        AccessLevel.freeTier => hint != null
            ? 'Free to keep using within its limits; paid plans lift them.'
            : 'The description or docs mention a free plan or usage limits - expect paid plans above them.',
        AccessLevel.trialOnly => hint != null
            ? 'The free part is a demo or trial - real use needs a paid plan.'
            : entry.pricing == 'paid'
                ? 'Flagged as paid / trial by the directory that listed it.'
                : 'The description or docs talk about trials or paid plans, with no free plan mentioned.',
        AccessLevel.unknown => 'The directories do not say what is free. \'Scan docs for key info\' may find out.',
      };
}
