# Load testing Telepathy and Async Tcp server

In this project I am load testing Telepathy and Async Tcp Server. 

# Methodology

The server was compiled with unity 2018.2.18f for linux 64bit headless.

To start a telepathy server:
```sh
./server.x86_64 Telepathy 9876
```
To start an async tcp server:
```sh
./server.x86_64 Telepathy 9876
```

the clients were run using jmeter,  in a separate computer going over wifi.

Both the server code and the jmeter test are included in this project.

An error is reported if a transaction takes more than 10 seconds or if it returns unexpected results

# Results

## 1 thread 

With a telepathy server I got this:
```sh
jmeter -n -t loadtest.jmx -JHOST=pc.local -JPORT=9876 -JTHREADS=1
summary = 500000 in 00:00:35 = 14129.9/s Avg:     5 Min:     1 Max:    45 Err:     0 (0.00%)
```

With a single thread client,  telepathy is able to process 14K messages per second using roughly 130-150% cpu.

With an async TCP server  I got this:

```sh
summary = 500000 in 00:00:38 = 13029.3/s Avg:     5 Min:     1 Max:   247 Err:     0 (0.00%)
```

The server used 250-280%  CPU during this time.  Clearly for a single thread client telepathy does better

## 10 threads

Telepathy:
```sh
jmeter -n -t loadtest.jmx -JHOST=pc.local -JPORT=9876 -JTHREADS=10
summary = 500000 in 00:00:40 = 12445.2/s Avg:     6 Min:     1 Max: 10013 Err:     2 (0.00%)
```
Telepathy had a 4 errors total,  140% CPU

Async TCP:
```sh
summary = 500000 in 00:00:39 = 12734.6/s Avg:     5 Min:     1 Max:   238 Err:     0 (0.00%)
```
No errors,  270% CPU,  bandwidth is the same

## 50 threads

Telepathy:
```sh
jmeter -n -t loadtest.jmx -JHOST=pc.local -JPORT=9876 -JTHREADS=10
summary = 500000 in 00:00:41 = 12342.0/s Avg:     6 Min:     1 Max: 10165 Err:    47 (0.01%)
```

Async TCP
```sh
summary = 500000 in 00:00:38 = 13245.4/s Avg:     5 Min:     1 Max:    75 Err:     0 (0.00%)
```
while bandwidth and cpu remain the same,  Async TCP exhibits a max latency of 75 ms  while Telepathy can go beyond 10 seconds.

## 100 threads

Telepathy:
```sh
summary = 500000 in 00:00:40 = 12413.7/s Avg:     6 Min:     1 Max: 10013 Err:    27 (0.01%)
```

Async TCP:
```sh
summary = 500000 in 00:00:38 = 13164.1/s Avg:     5 Min:     1 Max:   418 Err:     0 (0.00%)
```

## 300 threads