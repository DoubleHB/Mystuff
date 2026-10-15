// The request history and the saved test requests: sealed on disk (AES-256-GCM under a random key that the Android
// keystore holds), read on first use, written whole, one write after another. They are never re-sealed under a new
// key when they could not be opened - a healthy next launch must still find them whole.
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter/foundation.dart' show compute;
import 'package:path_provider/path_provider.dart';

import 'user_data.dart';
import 'vault.dart';

class SealedStore {
  /// The vault as it is now: the app swaps in a memory vault when the keystore will not open.
  final KeyVault Function() vaultOf;

  /// True while a memory vault stands in for the keystore: nothing is sealed into a key that is lost at close.
  final bool Function() vaultBroken;

  SealedStore({required this.vaultOf, required this.vaultBroken});

  /// How many APIs keep a history: beyond this the one not tested for longest is dropped, so the sealed file stays a
  /// few megabytes at most however many APIs have been tried over the years.
  static const historyApis = 100;

  Map<String, List<TestHistoryEntry>>? _history;
  Map<String, TestRequest>? _requests;
  Uint8List? _key;
  Future<void>? _loading;
  Future<void> _pending = Future.value();

  /// Set when sealed files exist that could not be opened (no seal key, a vault that would not open, or a key that does
  /// not fit): nothing is written to them this session, so a healthy next launch still finds them whole.
  String? problem;

  Future<File> _file(String name) async => File('${(await getApplicationSupportDirectory()).path}/$name');
  Future<File> get _historyFile => _file('history.bin');
  Future<File> get _requestsFile => _file('requests.bin');

  /// Read on first use, not at start-up: most visits never open it. One load at a time: a second caller waits for the
  /// first instead of seeing empty maps that a write could then persist.
  Future<void> ensure() => _loading ??= _load();

  Future<void> _load() async {
    final history = <String, List<TestHistoryEntry>>{};
    final requests = <String, TestRequest>{};
    try {
      final saved = vaultBroken() ? null : await vaultOf().readSecret('history-key');
      if (saved != null) _key = base64Decode(saved);
      final file = await _historyFile, requestsFile = await _requestsFile;
      final sealedExists = await file.exists() || await requestsFile.exists();
      if (_key == null) {
        if (sealedExists) problem = 'The saved test requests and history could not be unlocked this time. Nothing new is saved until they can be.';
      } else {
        var opened = true;
        if (await file.exists()) {
          final plain = await compute(openIsolate, [_key!, await file.readAsBytes()]);
          if (plain != null) {
            history.addAll(historyFromJson(jsonDecode(utf8.decode(plain))));
          } else {
            opened = false;
          }
        }
        if (await requestsFile.exists()) {
          final plain = await compute(openIsolate, [_key!, await requestsFile.readAsBytes()]);
          if (plain != null) {
            requests.addAll(testRequestsFromJson(jsonDecode(utf8.decode(plain))));
          } else {
            opened = false;
          }
        }
        if (!opened) problem = 'The saved test requests and history on this phone do not fit the key in its secure storage, so they are left as they are. Nothing new is saved to them.';
      }
    } catch (_) {
      problem ??= 'The saved test requests and history could not be read this time. Nothing new is saved until they can be.';
    }
    _history = history;
    _requests = requests;
  }

  // ---------------------------------------------------------------- history

  List<TestHistoryEntry> historyOf(String apiKey) => _history?[apiKey] ?? const [];

  Future<void> addHistory(String apiKey, TestHistoryEntry entry) async {
    await ensure();
    final list = _history!.putIfAbsent(apiKey, () => []);
    list.insert(0, entry);
    if (list.length > historyPerApi) list.removeRange(historyPerApi, list.length);
    while (_history!.length > historyApis) {
      final oldest = _history!.entries.reduce((a, b) => _newest(a.value).isBefore(_newest(b.value)) ? a : b);
      _history!.remove(oldest.key);
    }
    await _saveHistory();
  }

  static DateTime _newest(List<TestHistoryEntry> list) => list.isEmpty ? DateTime.fromMillisecondsSinceEpoch(0) : list.map((h) => h.at).reduce((a, b) => a.isAfter(b) ? a : b);

  Future<void> clearHistory(String apiKey) async {
    await ensure();
    if (_history!.remove(apiKey) != null) await _saveHistory();
  }

  Future<void> _saveHistory() => _writeSealed(_historyFile, jsonEncode({for (final e in _history!.entries) e.key: [for (final h in e.value) h.toJson()]}));

  // ---------------------------------------------------------------- saved test requests

  /// Every saved request, once [ensure] has run (empty before).
  Map<String, TestRequest> get requests => _requests ?? const {};

  /// The request last tested for this API when it differed from the suggested one; null = the suggested one.
  TestRequest? savedRequestOf(String apiKey) => _requests?[apiKey];

  /// Remembers [r] for the API, or forgets the saved one with null. Nothing is written when nothing changed.
  Future<void> setSavedRequest(String apiKey, TestRequest? r) async {
    await ensure();
    final have = _requests![apiKey];
    if (r == null ? have == null : have != null && have.sameAs(r)) return;
    r == null ? _requests!.remove(apiKey) : _requests![apiKey] = r;
    await _saveRequests();
  }

  /// Takes the requests of an import that this phone does not have yet (a request already saved here stays, the
  /// desktop's rule). Returns how many were taken.
  Future<int> adoptRequests(Map<String, TestRequest> incoming) async {
    await ensure();
    var taken = 0;
    for (final e in incoming.entries) {
      if (_requests!.containsKey(e.key)) continue;
      _requests![e.key] = e.value;
      taken++;
    }
    if (taken > 0) await _saveRequests();
    return taken;
  }

  Future<void> _saveRequests() => _writeSealed(_requestsFile, jsonEncode({for (final e in _requests!.entries) e.key: e.value.toJson()}));

  /// Seals [json] with the keystore-held key (made on first use) and writes it whole, one write after another, with
  /// the sealing itself off the UI isolate.
  Future<void> _writeSealed(Future<File> which, String json) => _pending = _pending.then((_) async {
        // never mint a new seal key over files sealed with one this session could not read, nor into a vault that
        // forgets at close: either would make the real files unreadable for good
        if (problem != null || (_key == null && vaultBroken())) return;
        try {
          if (_key == null) {
            _key = newSealKey();
            await vaultOf().writeSecret('history-key', base64Encode(_key!));
          }
          final file = await which;
          final temp = File('${file.path}.$pid.tmp');
          final sealed = await compute(sealIsolate, [_key!, Uint8List.fromList(utf8.encode(json))]);
          await temp.writeAsBytes(sealed, flush: true);
          await temp.rename(file.path);
        } catch (_) {
          // the history is a convenience: failing to save it must not get in the way of the test itself
        }
      });
}
