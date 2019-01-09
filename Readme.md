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

Server:  Linux,  i3-6100U @ 2.30 GHz  (2 core,  4 threads)

| Threads |  Tel Tp | Async Tp | Tel Lat | Async Lat | Tel CPU | Async CPU |
| ------: | ------: | -------: | ------: | --------: | ------: | --------: |
|       1 |     510 |      407 |     825 |       218 |     24% |       50% |
|      50 |   10571 |     9832 |    1610 |       417 |    130% |      250% |
|     250 |         |    16868 |         |       293 |         |      270% |
|     500 |         |    16728 |         |      1100 |         |      270% |
|    1000 |         |    16942 |         |      2737 |         |      280% |


## 1 thread 


With a telepathy server I got this:
```sh
$ jmeter -n -t loadtest.jmx -JHOST=pc.local -JPORT=9876 -JTHREADS=1
summary =  20000 in 00:00:39 =  510.9/s Avg:     1 Min:     1 Max:   825 Err:     0 (0.00%)
```

With a single thread client,  telepathy is able to process 510 messages per second.  The slowest message took 0.8 seconds. CPU at 24 %

With an async TCP server  I got this:

```sh
summary =  20000 in 00:00:49 =  407.8/s Avg:     2 Min:     1 Max:   218 Err:     0 (0.00%)
```
lower throughput.  Cpu at 50-60%,   but it had much better latency,  the worst package took only 0.2 seconds.

## 50 threads

Telepathy:
```sh
$ jmeter -n -t loadtest.jmx -JHOST=pc.local -JPORT=9876 -JTHREADS=50
summary = 1000000 in 00:01:35 = 10571.8/s Avg:     4 Min:     1 Max:  1610 Err:     0 (0.00%)
```
CPU usage 130-150%.  

Async TCP:
```sh
summary = 1000000 in 00:01:42 = 9832.3/s Avg:     4 Min:     1 Max:   417 Err:     0 (0.00%)
```
250% CPU

## 250 threads

Telepathy:
```sh
$ jmeter -n -t loadtest.jmx -JHOST=pc.local -JPORT=9876 -JTHREADS=250
summary =   5109 in 00:03:34 =   23.8/s Avg: 10005 Min: 10000 Max: 10372 Err:  5109 (100.00%)
```
at 250 threads,  Telepathy choke,  it was not able to reply to any message


Async TCP
```sh
summary = 5000000 in 00:04:56 = 16868.8/s Avg:    14 Min:     1 Max:   293 Err:     0 (0.00%)
```


## 500 threads

Telepathy not able to cope.

Async TCP:
```sh
summary = 5604554 in 00:05:35 = 16728.8/s Avg:    29 Min:     1 Max:  1100 Err:     0 (0.00%)
```

## 1000 threads

Telepathy not able to cope.

Async TCP:
```sh
summary = 10670731 in 00:10:30 = 16942.7/s Avg:    58 Min:     2 Max:  2737 Err:     0 (0.00%)
```
