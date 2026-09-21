// Data shapes shared by the scanner, the knowledge rules and the UI.
// They mirror the desktop app's ApiEntry / KeyHint so catalogues mean the same thing on both.

enum AuthKind { none, apiKey, oauth, other, unknown }

enum AccessLevel { fullFree, freeTier, trialOnly, unknown }

class ApiEntry {
  String key;
  String name;
  String description;
  String url;
  String category;
  String rawCategory;
  AuthKind auth;
  String authRaw;
  bool? https;
  String cors; // "Yes", "No", "Unknown" or ""
  String pricing; // "paid", "open" or ""
  int? health;
  List<String> sources;

  ApiEntry({
    this.key = '',
    required this.name,
    required this.url,
    this.description = '',
    this.category = '',
    this.rawCategory = '',
    this.auth = AuthKind.unknown,
    this.authRaw = '',
    this.https,
    this.cors = '',
    this.pricing = '',
    this.health,
    List<String>? sources,
  }) : sources = sources ?? [];

  Map<String, dynamic> toJson() => {
        'key': key,
        'name': name,
        'description': description,
        'url': url,
        'category': category,
        'rawCategory': rawCategory,
        'auth': auth.index,
        'authRaw': authRaw,
        'https': https,
        'cors': cors,
        'pricing': pricing,
        'health': health,
        'sources': sources,
      };

  factory ApiEntry.fromJson(Map<String, dynamic> j) => ApiEntry(
        key: j['key'] as String? ?? '',
        name: j['name'] as String? ?? '',
        url: j['url'] as String? ?? '',
        description: j['description'] as String? ?? '',
        category: j['category'] as String? ?? '',
        rawCategory: j['rawCategory'] as String? ?? '',
        auth: AuthKind.values[(j['auth'] as int? ?? AuthKind.unknown.index).clamp(0, AuthKind.values.length - 1)],
        authRaw: j['authRaw'] as String? ?? '',
        https: j['https'] as bool?,
        cors: j['cors'] as String? ?? '',
        pricing: j['pricing'] as String? ?? '',
        health: j['health'] as int?,
        sources: (j['sources'] as List?)?.cast<String>() ?? [],
      );
}

/// What ApiScout knows about getting a key for one provider (from the desktop app's KeyKnowledge).
class KeyHint {
  final List<String> hosts;
  final String howTo;
  final String? signupUrl;
  final String? demoKey;
  final String? keyUsage;
  final String? example;
  final bool keylessWorks;

  const KeyHint({required this.hosts, required this.howTo, this.signupUrl, this.demoKey, this.keyUsage, this.example, this.keylessWorks = false});

  factory KeyHint.fromJson(Map<String, dynamic> j) => KeyHint(
        hosts: (j['hosts'] as List).cast<String>(),
        howTo: j['howTo'] as String? ?? '',
        signupUrl: j['signupUrl'] as String?,
        demoKey: j['demoKey'] as String?,
        keyUsage: j['keyUsage'] as String?,
        example: j['example'] as String?,
        keylessWorks: j['keylessWorks'] as bool? ?? false,
      );
}

class Catalogue {
  final DateTime scannedAt;
  final List<ApiEntry> entries;
  final List<String> notes;
  Catalogue(this.scannedAt, this.entries, [this.notes = const []]);

  Map<String, dynamic> toJson() => {'scannedAt': scannedAt.toIso8601String(), 'entries': [for (final e in entries) e.toJson()]};

  factory Catalogue.fromJson(Map<String, dynamic> j) => Catalogue(
        DateTime.tryParse(j['scannedAt'] as String? ?? '') ?? DateTime.now(),
        [for (final e in (j['entries'] as List? ?? [])) ApiEntry.fromJson(e as Map<String, dynamic>)],
      );
}
