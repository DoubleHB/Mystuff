// "API of the day" as a phone notification at 9:00, scheduled a week ahead from the catalogue on the phone.
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:timezone/data/latest.dart' as tzdata;
import 'package:timezone/timezone.dart' as tz;

typedef DailyItem = ({DateTime when, String key, String title, String body});

class DailyNotice {
  static final _plugin = FlutterLocalNotificationsPlugin();
  static bool _ok = false;

  /// [onOpen] gets the API key behind a notification the user tapped while the app was running.
  static Future<void> init(void Function(String key) onOpen) async {
    try {
      tzdata.initializeTimeZones();
      await _plugin.initialize(
        settings: const InitializationSettings(android: AndroidInitializationSettings('@mipmap/ic_launcher')),
        onDidReceiveNotificationResponse: (r) {
          final key = r.payload;
          if (key != null && key.isNotEmpty) onOpen(key);
        },
      );
      _ok = true;
    } catch (_) {
      // no notifications on this device: the setting simply stays off
    }
  }

  /// The API key of the notification that started the app, if one did.
  static Future<String?> launchedWith() async {
    if (!_ok) return null;
    try {
      final d = await _plugin.getNotificationAppLaunchDetails();
      return d?.didNotificationLaunchApp == true ? d!.notificationResponse?.payload : null;
    } catch (_) {
      return null;
    }
  }

  static Future<bool> askPermission() async {
    if (!_ok) return false;
    try {
      final android = _plugin.resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>();
      return await android?.requestNotificationsPermission() ?? false;
    } catch (_) {
      return false;
    }
  }

  /// Replaces whatever is scheduled with these. Times are the phone's local wall clock; the plugin takes the
  /// instant, so the UTC zone is right for it.
  static Future<void> schedule(Iterable<DailyItem> items) async {
    if (!_ok) return;
    try {
      await _plugin.cancelAll();
      var id = 100;
      final now = DateTime.now();
      for (final it in items) {
        if (it.when.isBefore(now)) continue;
        await _plugin.zonedSchedule(
          id: id++,
          scheduledDate: tz.TZDateTime.from(it.when, tz.UTC),
          title: it.title,
          body: it.body,
          payload: it.key,
          notificationDetails: const NotificationDetails(
            android: AndroidNotificationDetails('api-of-the-day', 'API of the day', channelDescription: 'One free API a day, at 9:00'),
          ),
          androidScheduleMode: AndroidScheduleMode.inexactAllowWhileIdle,
        );
      }
    } catch (_) {}
  }

  static Future<void> cancelAll() async {
    if (!_ok) return;
    try {
      await _plugin.cancelAll();
    } catch (_) {}
  }
}
