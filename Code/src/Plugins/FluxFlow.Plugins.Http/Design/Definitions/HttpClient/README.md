# HTTP Client

Sends HTTP requests and emits their results. Configuration type: `http.client`.

## Setup

Set **Client** to a named `http.client` resource, such as `api`. The connection definition
holds the base address, authentication and retry policy. Project setup can use the real
service or a temporary WireMock service with configured endpoints.

## Ports and message value

**Input** receives a request description:

```json
{
  "RequestUrl": "/readings",
  "Method": "POST",
  "Body": {
    "Reading": 21.5
  }
}
```

Requests can include `Headers`. Use `Form` with `Form.Files` for multipart uploads.
**Output** contains the response or processing error; **Error** is a filtered Output.
**Events** is the component event port. Preserve the message envelope when mapping
requests and replies so correlated workflows can match responses.

HTTP 4xx/5xx responses remain ordinary values on Output; inspect `StatusCode` explicitly.
Network failures, timeouts and malformed requests become processing errors.
Response `Body` is text by default, including JSON responses; parse it downstream when needed.
`Body` and multipart `Form` cannot be supplied together.

`OutputCapacity` defaults to 128. This component calls a server; it does not expose
an inbound endpoint. Use HTTP Trigger to receive requests.
