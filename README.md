# 🔐 PassHunter

**PassHunter** is a low-performance, cross-format password brute-force tool built in C#.

---
# Test files
- test1.zip - password: c3a  - takes 15-60s
- test2.zip - password: d2t6 - takes 9-40m
- test3.zip - password: x7P. - takes 6-12h

---
# Performance
It is NOT optimal and it is not trying to be. The optimal approach is to obtain and crack the hash extracted from the file's metadata or header. PassHunter is like this because it started as a bet with a friend to see if it could crack his password without using hashes.

But for those interested in performance, here are some numbers:
- Printing time estimate: slows prog5am for about 1.5% - Also ETA: states the time it will take to try the current number of digits not max number you gave as argument.