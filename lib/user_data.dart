import 'dart:convert';
import 'dart:math';
import 'dart:typed_data';

import 'package:pointycastle/export.dart';

const maxTagsPerApi = 8;

/// What the user has added on top of the catalogue. Keys are catalogue keys (makeKey), the same on the desktop.
class UserData {
  Set<String> favourites = {};
  Map<String, List<String>> tags = {};
  Map<String, String> notes = {};
  Map<String, List<String>> collections = {};

  /// Request variables per API (name -> value). They travel with the keys in an export: someone may have put a token in one.
  Map<String, Map<String, String>> variables = {};

  UserData();

  Map<String, dynamic> toJson() => {'favourites': favourites.toList()..sort(), 'tags': tags, 'notes': notes, 'collections': collections, 'variables': variables};

  factory UserData.fromJson(Map<String, dynamic> j) => UserData()
    ..favourites = {..._strings(j['favourites'])}
    ..tags = _lists(j['tags'])
    ..notes = _texts(j['notes'])
    ..collections = _lists(j['collections'])
    ..variables = _maps(j['variables']);

  /// Every tag in use, most used first.
  List<(String, int)> get tagCounts {
    final counts = <String, int>{};
    final shown = <String, String>{};
    for (final list in tags.values) {
      for (final t in list) {
        final k = t.toLowerCase();
        counts[k] = (counts[k] ?? 0) + 1;
        shown.putIfAbsent(k, () => t);
      }
    }
    final result = [for (final e in counts.entries) (shown[e.key]!, e.value)];
    result.sort((a, b) => b.$2 != a.$2 ? b.$2.compareTo(a.$2) : a.$1.toLowerCase().compareTo(b.$1.toLowerCase()));
    return result;
  }
}

/// A Try it request edited away from the suggested one, remembered per API - the desktop's ApiTestRequest, with its
/// JSON names. A header may hold a token, so on the phone it is sealed like the history, and in an export it travels
/// only inside the encrypted part.
class TestRequest {
  final String method, url, headers, body;
  const TestRequest(this.method, this.url, this.headers, this.body);

  Map<String, dynamic> toJson() => {'Method': method, 'Url': url, 'Headers': headers, 'Body': body};

  /// The desktop's names or this app's; null without a URL.
  static TestRequest? fromJson(dynamic j) {
    if (j is! Map) return null;
    String s(String a, String b) => j[a] is String ? j[a] as String : j[b] is String ? j[b] as String : '';
    final url = s('Url', 'url').trim();
    if (url.isEmpty) return null;
    final method = s('Method', 'method').trim().toUpperCase();
    return TestRequest(method.isEmpty ? 'GET' : method, url, s('Headers', 'headers'), s('Body', 'body'));
  }

  bool sameAs(TestRequest o) => method == o.method && url == o.url && headers == o.headers && body == o.body;
}

Map<String, TestRequest> testRequestsFromJson(dynamic j) {
  if (j is! Map) return {};
  final out = <String, TestRequest>{};
  for (final e in j.entries) {
    final r = TestRequest.fromJson(e.value);
    if (e.key is String && r != null) out[e.key as String] = r;
  }
  return out;
}

List<String> _strings(dynamic v) => v is List ? [for (final s in v) if (s is String && s.isNotEmpty) s] : [];
Map<String, List<String>> _lists(dynamic v) => v is Map ? {for (final e in v.entries) if (e.key is String && e.value is List) e.key as String: _strings(e.value)} : {};
Map<String, Map<String, String>> _maps(dynamic v) => v is Map ? {for (final e in v.entries) if (e.key is String && e.value is Map) e.key as String: _texts(e.value)} : {};
Map<String, String> _texts(dynamic v) => v is Map ? {for (final e in v.entries) if (e.key is String && e.value is String) e.key as String: e.value as String} : {};

/// "a, b; c" → tags: trimmed, no duplicates (whatever the case), at most eight, each at most 30 characters.
List<String> parseTags(String text) {
  final seen = <String>{};
  final list = <String>[];
  for (final raw in text.split(RegExp(r'[,;\n]'))) {
    var t = raw.trim().replaceAll(RegExp(r'\s+'), ' ');
    if (t.startsWith('#')) t = t.substring(1).trim();
    if (t.length > 30) t = t.substring(0, 30).trim();
    if (t.isNotEmpty && seen.add(t.toLowerCase())) list.add(t);
    if (list.length == maxTagsPerApi) break;
  }
  return list;
}

// ---------------------------------------------------------------- the desktop app's export file (Services/Backup.cs)

class BackupFormatException implements Exception {
  final String message;
  BackupFormatException(this.message);
  @override
  String toString() => message;
}

class WrongPassphraseException implements Exception {
  @override
  String toString() => 'Wrong passphrase (or the file is damaged).';
}

/// The "move my data" file both apps read and write. Favourites, tags, notes and collections are in the clear;
/// saved keys are inside [secrets]: AES-256-GCM under a PBKDF2-SHA256 key from the passphrase, or absent.
class BackupFile {
  UserData data = UserData();
  DateTime? exportedAt;
  String? secrets;
  String? salt;
  int iterations = 0;
  int secretCount = 0;

  static const defaultIterations = 310000, maxIterations = 5000000;

  bool get hasSecrets => secrets != null && secrets!.isNotEmpty;

  static BackupFile read(String json) {
    dynamic j;
    try {
      j = jsonDecode(json.isNotEmpty && json.codeUnitAt(0) == 0xFEFF ? json.substring(1) : json); // a byte-order mark from a Windows editor
    } on FormatException {
      j = null;
    }
    if (j is! Map || j['App'] != 'ApiScout') throw BackupFormatException('This is not an API Free export file.');
    final file = BackupFile()
      ..exportedAt = DateTime.tryParse(j['ExportedAt'] as String? ?? '')
      ..secrets = j['Secrets'] as String?
      ..salt = j['Salt'] as String?
      ..iterations = j['Iterations'] is int ? j['Iterations'] as int : 0
      ..secretCount = j['SecretCount'] is int ? j['SecretCount'] as int : 0;
    file.data
      ..favourites = {..._strings(j['Favourites'])}
      ..tags = {for (final e in _lists(j['Tags']).entries) e.key: [for (final t in e.value) if (t.trim().isNotEmpty) t]}
      ..notes = _texts(j['Notes'])
      ..collections = {for (final e in _lists(j['Collections']).entries) if (e.key.trim().isNotEmpty) e.key: e.value};
    // the count comes from the file: refuse one that would keep the phone busy for an hour
    if (file.hasSecrets && (file.iterations < 0 || file.iterations > maxIterations)) {
      throw BackupFormatException('The export file asks for an unreasonable amount of key stretching - it is damaged or not from API Free.');
    }
    return file;
  }

  /// The saved keys inside the file (catalogue key → API key).
  Map<String, String> openKeys(String passphrase) => openSecrets(passphrase).$1;

  /// Keys, request variables and saved test requests inside the file. Slow on purpose (key stretching): call it through compute().
  (Map<String, String>, Map<String, Map<String, String>>, Map<String, TestRequest>) openSecrets(String passphrase) {
    if (!hasSecrets) return ({}, {}, {});
    final Uint8List sealed, saltBytes;
    try {
      sealed = base64Decode(secrets!);
      saltBytes = base64Decode(salt ?? '');
    } on FormatException {
      throw BackupFormatException('The encrypted part of the file is damaged.');
    }
    if (sealed.length < 28 || saltBytes.isEmpty) throw BackupFormatException('The encrypted part of the file is damaged.');
    final plain = _open(sealed, passphrase, saltBytes, iterations > 0 ? iterations : defaultIterations);
    dynamic j;
    try {
      j = jsonDecode(utf8.decode(plain));
    } on FormatException {
      j = null;
    }
    if (j is! Map) throw BackupFormatException('The encrypted part of the file opened, but what is inside is not API Free data.');
    return (_texts(j['MyKeys'])..removeWhere((_, v) => v.isEmpty), _maps(j['Variables']), testRequestsFromJson(j['TestRequests']));
  }

  /// A file the desktop app's Import reads. [keys], the request variables and the saved test [requests] go in only with a passphrase.
  static String write(UserData data,
      {Map<String, String> keys = const {}, Map<String, TestRequest> requests = const {}, String? passphrase, int iterations = defaultIterations, DateTime? now, Random? random}) {
    final j = <String, dynamic>{
      'App': 'ApiScout',
      'Version': 1,
      'ExportedAt': (now ?? DateTime.now()).toIso8601String(),
      'Favourites': data.favourites.toList()..sort(),
      'Tags': data.tags,
      'Notes': data.notes,
      'Collections': data.collections,
      'Secrets': null,
      'Salt': null,
      'Iterations': 0,
      'SecretCount': 0,
    };
    if (passphrase != null && passphrase.isNotEmpty && (keys.isNotEmpty || data.variables.isNotEmpty || requests.isNotEmpty)) {
      final rnd = random ?? Random.secure();
      Uint8List bytes(int n) => Uint8List.fromList([for (var i = 0; i < n; i++) rnd.nextInt(256)]);
      final saltBytes = bytes(16);
      final plain = utf8.encode(jsonEncode({'MyKeys': keys, 'TestRequests': {for (final e in requests.entries) e.key: e.value.toJson()}, 'Variables': data.variables}));
      j['Salt'] = base64Encode(saltBytes);
      j['Iterations'] = iterations;
      j['SecretCount'] = keys.length + requests.length; // the desktop's count: keys and saved requests
      j['Secrets'] = base64Encode(_seal(Uint8List.fromList(plain), passphrase, saltBytes, iterations, bytes(12)));
    }
    return const JsonEncoder.withIndent('  ').convert(j);
  }
}

Uint8List _stretch(String passphrase, Uint8List salt, int iterations) => pbkdf2Sha256(Uint8List.fromList(utf8.encode(passphrase)), salt, iterations);

// ---- PBKDF2-HMAC-SHA256, 32 bytes out.
// The general-purpose version in pointycastle took 15-25 s for the desktop's 310,000 rounds on a phone. This one keeps
// the two HMAC pad states and does exactly two SHA-256 compressions a round on fixed-shape blocks, with no allocation.
// The test opens a real desktop export with it, so it is checked against .NET's Rfc2898DeriveBytes.

const _k = [
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, //
  0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da, 0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
  0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
];
const _iv = [0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19];
const _m = 0xFFFFFFFF;

/// One SHA-256 compression: [w] holds the block in its first 16 words (the rest is scratch), [from] is the state before, [to] the state after.
void _compress(Uint32List from, Uint32List w, Uint32List to) {
  for (var i = 16; i < 64; i++) {
    final x = w[i - 15], y = w[i - 2];
    w[i] = w[i - 16] + (((x >>> 7) | (x << 25)) ^ ((x >>> 18) | (x << 14)) ^ (x >>> 3)) + w[i - 7] + (((y >>> 17) | (y << 15)) ^ ((y >>> 19) | (y << 13)) ^ (y >>> 10));
  }
  var a = from[0], b = from[1], c = from[2], d = from[3], e = from[4], f = from[5], g = from[6], h = from[7];
  for (var i = 0; i < 64; i++) {
    final s1 = ((e >>> 6) | (e << 26)) ^ ((e >>> 11) | (e << 21)) ^ ((e >>> 25) | (e << 7));
    final t1 = (h + (s1 & _m) + ((e & f) ^ (~e & _m & g)) + _k[i] + w[i]) & _m;
    final s0 = ((a >>> 2) | (a << 30)) ^ ((a >>> 13) | (a << 19)) ^ ((a >>> 22) | (a << 10));
    final t2 = ((s0 & _m) + ((a & b) ^ (a & c) ^ (b & c))) & _m;
    h = g;
    g = f;
    f = e;
    e = (d + t1) & _m;
    d = c;
    c = b;
    b = a;
    a = (t1 + t2) & _m;
  }
  to[0] = from[0] + a;
  to[1] = from[1] + b;
  to[2] = from[2] + c;
  to[3] = from[3] + d;
  to[4] = from[4] + e;
  to[5] = from[5] + f;
  to[6] = from[6] + g;
  to[7] = from[7] + h;
}

Uint8List pbkdf2Sha256(Uint8List password, Uint8List salt, int iterations) {
  // the first round has a message of any length (salt + block number): the library does that one
  final hmac = HMac(SHA256Digest(), 64)..init(KeyParameter(password));
  final u1 = hmac.process(Uint8List.fromList([...salt, 0, 0, 0, 1]));

  // HMAC pad states: the hash state after the 64-byte key block
  final key = Uint8List(64)..setAll(0, password.length > 64 ? SHA256Digest().process(password) : password);
  final w = Uint32List(64), inner = Uint32List(8), outer = Uint32List(8), iv = Uint32List.fromList(_iv);
  void padState(int pad, Uint32List into) {
    for (var i = 0; i < 16; i++) {
      w[i] = ((key[i * 4] ^ pad) << 24) | ((key[i * 4 + 1] ^ pad) << 16) | ((key[i * 4 + 2] ^ pad) << 8) | (key[i * 4 + 3] ^ pad);
    }
    _compress(iv, w, into);
  }

  padState(0x36, inner);
  padState(0x5c, outer);

  final u = Uint32List(8), t = Uint32List(8), mid = Uint32List(8);
  final u1Words = ByteData.sublistView(u1);
  for (var i = 0; i < 8; i++) {
    u[i] = t[i] = u1Words.getUint32(i * 4);
  }
  // every later round hashes exactly 32 bytes after the key block: 8 words, the 1 bit, zeros, and the length (64 + 32 bytes = 768 bits)
  for (var round = 1; round < iterations; round++) {
    w.setRange(0, 8, u);
    w[8] = 0x80000000;
    w.fillRange(9, 15, 0);
    w[15] = 768;
    _compress(inner, w, mid);
    w.setRange(0, 8, mid);
    w[8] = 0x80000000;
    w.fillRange(9, 15, 0);
    w[15] = 768;
    _compress(outer, w, u);
    for (var i = 0; i < 8; i++) {
      t[i] ^= u[i];
    }
  }
  final out = ByteData(32);
  for (var i = 0; i < 8; i++) {
    out.setUint32(i * 4, t[i]);
  }
  return out.buffer.asUint8List();
}

// layout (the desktop's): 12-byte nonce | 16-byte tag | ciphertext.   pointycastle's GCM works with ciphertext | tag.
Uint8List _seal(Uint8List plain, String passphrase, Uint8List salt, int iterations, Uint8List nonce) {
  final gcm = GCMBlockCipher(AESEngine())..init(true, AEADParameters(KeyParameter(_stretch(passphrase, salt, iterations)), 128, nonce, Uint8List(0)));
  final out = gcm.process(plain);
  final cipher = out.sublist(0, out.length - 16), tag = out.sublist(out.length - 16);
  return Uint8List.fromList([...nonce, ...tag, ...cipher]);
}

Uint8List _open(Uint8List sealed, String passphrase, Uint8List salt, int iterations) {
  final nonce = sealed.sublist(0, 12), tag = sealed.sublist(12, 28), cipher = sealed.sublist(28);
  final gcm = GCMBlockCipher(AESEngine())..init(false, AEADParameters(KeyParameter(_stretch(passphrase, salt, iterations)), 128, nonce, Uint8List(0)));
  try {
    return gcm.process(Uint8List.fromList([...cipher, ...tag]));
  } on InvalidCipherTextException {
    throw WrongPassphraseException();
  }
}

/// compute() entry points: key stretching takes seconds on a phone.
Map<String, dynamic> openSecretsIsolate(Map<String, String> args) {
  final (keys, variables, requests) = BackupFile.read(args['json']!).openSecrets(args['passphrase']!);
  return {'keys': keys, 'variables': variables, 'requests': {for (final e in requests.entries) e.key: e.value.toJson()}};
}
String writeBackupIsolate(Map<String, dynamic> args) => BackupFile.write(UserData.fromJson((args['data'] as Map).cast<String, dynamic>()),
    keys: (args['keys'] as Map).cast<String, String>(), requests: testRequestsFromJson(args['requests']), passphrase: args['passphrase'] as String?);

class ImportSummary {
  int favourites = 0, tagged = 0, notes = 0, keys = 0, variables = 0, requests = 0, keptLocal = 0, collectionEntries = 0, notInCatalogue = 0;
  bool secretsSkipped = false;

  @override
  String toString() => '${collectionEntries > 0 ? '$collectionEntries collection entr${collectionEntries == 1 ? 'y' : 'ies'} added. ' : ''}'
      'Imported $favourites favourite(s), tags for $tagged API(s), $notes note(s), $keys key(s), $variables request variable(s) and $requests saved test request(s).'
      '${keptLocal > 0 ? ' $keptLocal item(s) already on this phone were kept as they are.' : ''}'
      '${secretsSkipped ? ' The saved keys, variables and test requests in the file were skipped (no passphrase given).' : ''}'
      '${notInCatalogue > 0 ? ' $notInCatalogue of them are not in this phone\'s catalogue yet - scan to see them.' : ''}';
}

/// Merges into what is already there: nothing on this phone is overwritten or removed (the desktop's rules).
ImportSummary mergeUserData(UserData mine, UserData theirs) {
  final s = ImportSummary();
  for (final f in theirs.favourites) {
    if (mine.favourites.add(f)) s.favourites++;
  }
  theirs.tags.forEach((key, tags) {
    final have = mine.tags[key] ?? const <String>[];
    final seen = {for (final t in have) t.toLowerCase()};
    final merged = [...have, for (final t in tags) if (seen.add(t.toLowerCase())) t].take(maxTagsPerApi).toList();
    if (merged.length > have.length) {
      mine.tags[key] = merged;
      s.tagged++;
    }
  });
  theirs.notes.forEach((key, note) {
    final local = mine.notes[key] ?? '';
    if (local.isEmpty) {
      if (note.isNotEmpty) {
        mine.notes[key] = note;
        s.notes++;
      }
    } else if (local != note) {
      s.keptLocal++;
    }
  });
  theirs.collections.forEach((name, keys) {
    // same name = same collection: what the file has is added to the end
    final existing = mine.collections.keys.where((k) => k.toLowerCase() == name.toLowerCase());
    final have = mine.collections.putIfAbsent(existing.isEmpty ? name : existing.first, () => []);
    for (final k in keys) {
      if (!have.contains(k)) {
        have.add(k);
        s.collectionEntries++;
      }
    }
  });
  return s;
}

/// Variables from an import: a value already set on this phone stays (the desktop's rule). Returns how many were added.
int mergeVariables(UserData mine, Map<String, Map<String, String>> theirs) {
  var added = 0;
  theirs.forEach((api, map) {
    final have = mine.variables.putIfAbsent(api, () => {});
    map.forEach((name, value) {
      if (!have.keys.any((k) => k.toLowerCase() == name.toLowerCase())) {
        have[name] = value;
        added++;
      }
    });
    if (have.isEmpty) mine.variables.remove(api);
  });
  return added;
}

// ---------------------------------------------------------------- request history

const historyPerApi = 8;
const _historyResponseChars = 20000;

/// One earlier "Test this API" result. The request is kept as typed, so {key} and {variables} stay placeholders.
class TestHistoryEntry {
  final DateTime at;
  final String method, url, headers, body, summary, response;
  final bool ok;
  TestHistoryEntry({required this.at, required this.method, required this.url, this.headers = '', this.body = '', required this.ok, required this.summary, String response = ''})
      : response = response.length > _historyResponseChars ? '${response.substring(0, _historyResponseChars)}\n\n… cut for the history' : response;

  static const _months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  String get when => '${at.day} ${_months[at.month - 1]} ${at.hour.toString().padLeft(2, '0')}:${at.minute.toString().padLeft(2, '0')}';
  String get firstLine => summary.split('\n').first;

  Map<String, dynamic> toJson() => {'at': at.toIso8601String(), 'method': method, 'url': url, 'headers': headers, 'body': body, 'ok': ok, 'summary': summary, 'response': response};
  factory TestHistoryEntry.fromJson(Map<String, dynamic> j) => TestHistoryEntry(
        at: DateTime.tryParse(j['at'] as String? ?? '') ?? DateTime.fromMillisecondsSinceEpoch(0),
        method: j['method'] as String? ?? 'GET',
        url: j['url'] as String? ?? '',
        headers: j['headers'] as String? ?? '',
        body: j['body'] as String? ?? '',
        ok: j['ok'] == true,
        summary: j['summary'] as String? ?? '',
        response: j['response'] as String? ?? '',
      );
}

Map<String, List<TestHistoryEntry>> historyFromJson(dynamic j) => j is Map
    ? {
        for (final e in j.entries)
          if (e.key is String && e.value is List) e.key as String: [for (final h in e.value as List) if (h is Map) TestHistoryEntry.fromJson(h.cast<String, dynamic>())]
      }
    : {};

// A typed header may hold a token, so the history file is sealed (AES-256-GCM) with a random key that lives in the
// Android keystore - the desktop does the same with Windows DPAPI.   layout: 12-byte nonce | ciphertext | 16-byte tag

Uint8List newSealKey([Random? random]) {
  final rnd = random ?? Random.secure();
  return Uint8List.fromList([for (var i = 0; i < 32; i++) rnd.nextInt(256)]);
}

Uint8List sealWithKey(Uint8List key, Uint8List plain, [Random? random]) {
  final rnd = random ?? Random.secure();
  final nonce = Uint8List.fromList([for (var i = 0; i < 12; i++) rnd.nextInt(256)]);
  final gcm = GCMBlockCipher(AESEngine())..init(true, AEADParameters(KeyParameter(key), 128, nonce, Uint8List(0)));
  return Uint8List.fromList([...nonce, ...gcm.process(plain)]);
}

/// Null when the key is not the one the data was sealed with (or the data is damaged).
Uint8List? openWithKey(Uint8List key, Uint8List sealed) {
  if (sealed.length < 28 || key.length != 32) return null;
  final gcm = GCMBlockCipher(AESEngine())..init(false, AEADParameters(KeyParameter(key), 128, sealed.sublist(0, 12), Uint8List(0)));
  try {
    return gcm.process(sealed.sublist(12));
  } on InvalidCipherTextException {
    return null;
  }
}
