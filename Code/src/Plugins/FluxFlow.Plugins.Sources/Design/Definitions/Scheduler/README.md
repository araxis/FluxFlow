# Scheduler

Produces signals on a schedule. Configuration type: `scheduler`.
Use it to trigger a Source, Data Generator or another operation periodically.

## Setup

Choose a **Schedule** in the Property Grid:

- **Interval**: a duration, such as `00:00:01` for one second.
- **Cron**: a recurring calendar schedule. **Time zone** defaults to UTC;
  enable **Include seconds** only for a cron expression with a seconds field.
- **Once**: enter **At**, a date/time including its UTC offset.

The selector uses the existing `Interval`, `Cron` and `At` fields; it adds no saved
mode property. A new Interval starts at one second; a new Cron starts at every minute
(every second if Include seconds is already enabled). Once starts with an empty At
that must be filled before running. Switching removes the other schedule fields and,
when leaving Cron, Time zone and Include seconds. Undo restores the complete previous
settings in one step. Selecting the current mode does not change saved values.

Existing conflicting schedules stay visible for correction. Choosing one explicitly
retains its saved value and removes the conflicting fields. Unknown configuration
properties are preserved.

The Property Grid validates positive intervals and cron syntax with the existing
schedule parser. Enable **Include seconds** before entering a six-field expression.
Changing that option rechecks the saved expression; it does not rewrite the expression.
Invalid text stays in the editor for correction, without changing the saved value.

Time zone identifiers belong to the execution host. Enter the runner's identifier;
the editor does not replace it with a browser-local choice. The runner checks the
identifier against its own time zone database. Cron uses the existing Cronos daylight
saving rules: a daily occurrence in a skipped spring hour moves to the next valid time;
a daily occurrence in a repeated autumn hour fires once. At uses its explicit offset,
independently of the Cron time zone.

## Ports

There is no Input. **Output** carries scheduled signals; link it to the next component's
Input. Each signal has a JSON null value and a new message identity.
**Events** carries component events. **Error** is Studio's filtered Output view.

Interval scheduling waits one interval before the first tick. An expired Scheduled
time emits nothing. Missed occurrences are skipped rather than replayed in a burst.

Scheduling runs with the workflow lifecycle, not while editing the canvas.
`OutputCapacity` defaults to 128. A scheduler supplies timing; use a downstream Source
or Mapper when you need a specific message value.
