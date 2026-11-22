# JSONata Mapper

`workflow.JsonataMapper("total", "$sum(items.(quantity * unitCents))")` registers a Mapper with Input, Output and Events ports. The expression is compiled when the application builds. HandleErrors defaults to false and OutputCapacity defaults to 128.
