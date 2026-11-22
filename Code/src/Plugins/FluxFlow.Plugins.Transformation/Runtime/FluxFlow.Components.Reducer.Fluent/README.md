# Reducer

Use `workflow.Reduce("sum", "0"u8.ToArray(), "$state + $input", "$previous = $current")` to accumulate numbers and emit changed states. The initial JSON value and JSONata expressions are embedded by the application generator.
