# Transformation and aggregation

Install this package to use Mapper, Reducer and Batch. Mapper evaluates the configured expression for each input. Reducer maintains state for one run and emits changes. Batch preserves the original message envelopes when collecting by count, maximum wait or graceful completion.

The shared expression editor uses the project's saved engine. The execution host supplies that engine and resolves file references. Opening the project does not read those files.

Reducer previews use editable state/input or previous/current JSON samples through the portable preview contract. The plugin shares expression compilation and variable bindings with runtime; Studio supplies the common editor and result display. Preview has no persistent state and does not read Initial state files.
