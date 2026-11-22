# Deduplicate

Suppresses messages whose selected key was already seen within a retention period.
Configuration type: `deduplicate`.

## Setup

Set **Engine** and a string **Key selector**, then choose a positive **Retention**
duration, for example `00:05:00`. **Maximum keys** bounds remembered keys (default 1024).
**Ignore case** defaults to false and affects key comparison only.
Retention begins when a key passes; duplicates do not extend it. When capacity is
full, the oldest key is evicted and can pass again before its retention expires.
The Property Grid rejects blank, zero and negative retention drafts. There is no
timer-based upper bound: any positive TimeSpan is accepted by the existing contract.
An invalid saved duration remains visible for explicit repair instead of being reset.

## Ports

**Input** receives candidate messages; **Output** emits those that pass deduplication.
**Events** is the component event port; **Error** filters Output errors.
`OutputCapacity` defaults to 128.

Use Change Filter when you only need to detect changes between successive keys.
Deduplication is component state, not a durable delivery guarantee.
