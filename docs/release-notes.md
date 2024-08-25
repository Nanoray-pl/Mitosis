[← back to readme](README.md)

# Release notes

## Upcoming release

* Fixed parameterless constructors not being utilized.

## 2.0.4
Released 25 August 2024.

* Fixed issues cloning some types (mainly subclasses).

## 2.0.3
Released 24 August 2024.

* Mitosis no longer attempts to clone delegates, and instead just reuses them. While it's technically not a real clone, it's probably safer behavior than outright ignoring them.

## 2.0.2
Released 23 August 2024.

* Fixed cloned objects not getting their base type fields cloned.

## 2.0.1
Released 5 August 2024.

* Fixed reference cycles causing `StackOverflowException`s.

## 2.0.0
Released 5 August 2024.

* Renamed the listener method.

## 1.0.0
Released 5 August 2024.

* Initial release.