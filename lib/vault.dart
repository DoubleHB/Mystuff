import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Where the user's own API keys live. On the phone that is the Android keystore (through flutter_secure_storage):
/// the values are encrypted with a key that never leaves the device's secure hardware, and they are left out of
/// Android's cloud backup. Nothing else in the app stores a key - not the catalogue, not the settings, not the history.
abstract class KeyVault {
  Future<Set<String>> names();
  Future<String?> read(String apiKey);
  Future<void> write(String apiKey, String value);
  Future<void> delete(String apiKey);
  Future<Map<String, String>> readAll();
}

class SecureKeyVault implements KeyVault {
  static const _prefix = 'mykey:';
  final _storage = const FlutterSecureStorage();

  @override
  Future<Map<String, String>> readAll() async =>
      {for (final e in (await _storage.readAll()).entries) if (e.key.startsWith(_prefix) && e.value.isNotEmpty) e.key.substring(_prefix.length): e.value};

  @override
  Future<Set<String>> names() async => (await readAll()).keys.toSet();
  @override
  Future<String?> read(String apiKey) => _storage.read(key: '$_prefix$apiKey');
  @override
  Future<void> write(String apiKey, String value) => _storage.write(key: '$_prefix$apiKey', value: value);
  @override
  Future<void> delete(String apiKey) => _storage.delete(key: '$_prefix$apiKey');
}

/// For tests, and the fallback when the keystore cannot be opened (keys then last until the app closes).
class MemoryKeyVault implements KeyVault {
  final _map = <String, String>{};
  @override
  Future<Map<String, String>> readAll() async => {..._map};
  @override
  Future<Set<String>> names() async => _map.keys.toSet();
  @override
  Future<String?> read(String apiKey) async => _map[apiKey];
  @override
  Future<void> write(String apiKey, String value) async => _map[apiKey] = value;
  @override
  Future<void> delete(String apiKey) async => _map.remove(apiKey);
}
