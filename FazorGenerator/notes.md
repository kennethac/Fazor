# Notes

## You have to have a variable of the right type to pass in

If you pass a string literal to the markup in the place of say an int, it will work for components generated entirely by Razor but not with mine. This seems to be because the Razor generator doesn't know what to do with those values since the proeprty is not available to the source generator on the target type (the child component). To get around this I'd have to replace the razor generator entirely I think.