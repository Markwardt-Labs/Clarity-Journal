# Reminders

`IReminderSchedule`, `Reminders`, `ReminderNotifier`, `ReminderReceiver` and `ReminderRescheduler` remind the user at noon and at 8 pm, every day, to write the journal.

## Local, not pushed

The reminders are notifications the phone raises by itself from alarms it holds, not messages pushed from a server. The journal server may be off or unreachable, and a reminder to write must still arrive, so nothing about it goes through the server or a cloud push service. The cost is that the app cannot know what the server holds: the reminder comes even on a day the entry is already written.

## The schedule

`ReminderSchedule` is pure date arithmetic in the phone's local time: given a moment, it returns the first reminder strictly after it, with the text for that slot (a midday nudge and an evening "look back on the day"). It is in the shared frontend library so it can be tested without a device.

## One alarm at a time

Android alarms do not repeat reliably while the device sleeps, so `Reminders` keeps exactly one alarm, for the next reminder, replacing it each time. When it fires, `ReminderReceiver` shows the notification and schedules the one after. The alarm is allowed to fire while the device is idle but is not an exact alarm, which would need a special permission; a reminder can arrive a few minutes late in deep sleep.

## Staying scheduled

The system forgets alarms in a few cases, so `ReminderRescheduler` schedules again after the device boots, after the app is updated and when the clock or time zone changes, and the app schedules every time it starts, which also covers a force stop. The notification permission, required on Android 13 and later, is requested at the first start; without it the system drops the notifications silently, and so they are also controlled in the system settings (a channel named Journal reminders), which is the only way to turn them off.

## Opening the app

Tapping the notification starts the app, or brings it to the front, without changing the screen: a fresh start opens on the Journal tab with today's entry, and an app that was running stays on its current tab.
