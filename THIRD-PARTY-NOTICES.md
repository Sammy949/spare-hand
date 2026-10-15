# Third-party notices

Spare Hand includes or depends on the following. Thank you to everyone who built them.

## Included source

### PowerToys: `AlwaysActiveDesktopAcrylicBackdrop`

`src/SpareHand/Backdrops/AlwaysActiveDesktopAcrylicBackdrop.cs` is copied from
[microsoft/PowerToys](https://github.com/microsoft/PowerToys) (`src/common/Common.UI.Controls/Backdrops`). Only the
namespace was changed. Other code follows patterns documented in the same repository (the transient-surface window
chrome, Command Palette's cloaked window model, Quick Access' idle memory trim) and was written for Spare Hand.

> The MIT License
>
> Copyright (c) Microsoft Corporation. All rights reserved.
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
> documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
> rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
> persons to whom the Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
> Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
> WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
> COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
> OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## NuGet dependencies

| Package | License |
|---|---|
| [Microsoft.WindowsAppSDK](https://github.com/microsoft/WindowsAppSDK) | MIT, Copyright (c) Microsoft Corporation |
| [Microsoft.Windows.SDK.BuildTools](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools) | Microsoft license (build-time only) |
| [WinUIEx](https://github.com/dotMorten/WinUIEx) | MIT, Copyright (c) Morten Nielsen |
| [CommunityToolkit.WinUI.Controls.SettingsControls](https://github.com/CommunityToolkit/Windows) | MIT, Copyright (c) .NET Foundation and Contributors |

## Protocols and integrations

- **LocalSend**: Spare Hand implements the [LocalSend protocol](https://github.com/localsend/protocol) from its public
  specification. No LocalSend code is included.
- **Quick Share**: Spare Hand invokes Quick Share's own Explorer context-menu command when it's installed. No Quick
  Share code is included.

Spare Hand is not affiliated with or endorsed by Microsoft, Google, or the LocalSend project.
