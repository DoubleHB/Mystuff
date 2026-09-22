// apiscout://open/api/<key> opens the detail page from a shared text or a notification.

const _prefix = '/api/';

/// The link for one API. The key is a host + path ("thecocktaildb.com/api.php"), so it goes in encoded.
String deepLinkFor(String key) => 'apiscout://open/api/${Uri.encodeComponent(key)}';

/// The API key in a route Android handed the app ("/api/thecocktaildb.com/api.php", decoded or not), or null.
String? apiKeyFromRoute(String? route) {
  if (route == null || !route.startsWith(_prefix)) return null;
  var rest = route.substring(_prefix.length);
  final q = rest.indexOf('?');
  if (q >= 0) rest = rest.substring(0, q);
  try {
    rest = Uri.decodeComponent(rest);
  } catch (_) {
    // not encoded: use as it is
  }
  return rest.isEmpty ? null : rest;
}
