# SharpChangeLog

This project can be used to generate a *change log*, by diff'ing the previous release-branch with the trunk that becomes the next release-branch. The extracted commits are parsed for Redmine issues, which are then fetched from a Redmine instance and outputted.

Bare in mind that this is a quick'n'dirty project, so no fail-safe parsing of parameters and alike is done.

## Example usage


    SharpChangeLog.exe^
	  --repository=http://SVNSERVER/svn/REPOSITORY/^
	  --trunk=trunk/^
	  --branch=branch/release_XY/^
	  --redmineHost=http://SERVER/redmine^
	  --redmineApiKey=????????????????????????????????????????^
	  > changelog.txt

## License

SharpChangeLog is published under [WTFNMFPLv3](http://andreas.niedermair.name/introducing-wtfnmfplv3).

## Utilities

- [morelinq](https://github.com/morelinq/MoreLINQ)
- [NDesk.Options](http://www.ndesk.org/Options)
- [redmine-api](https://github.com/zapadi/redmine-net-api)
- [SharpSvn](https://sharpsvn.open.collab.net/)