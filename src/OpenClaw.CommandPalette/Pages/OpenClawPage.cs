// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace OpenClaw;

internal sealed partial class OpenClawPage : ListPage
{
    public OpenClawPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "OpenClaw";
        Name = "Open";
    }

    public override IListItem[] GetItems()
    {
        return [
            new ListItem(new OpenUrlCommand("http://localhost:18789"))
            {
                Title = "🦞 Open Dashboard",
                Subtitle = "Open OpenClaw web dashboard"
            },
            new ListItem(new OpenUrlCommand("openclaw://chat"))
            {
                Title = "💬 Web Chat",
                Subtitle = "Open the OpenClaw chat window"
            },
            new ListItem(new OpenUrlCommand("openclaw://send"))
            {
                Title = "📝 Quick Send", 
                Subtitle = "Send a message to OpenClaw"
            },
            new ListItem(new OpenUrlCommand("openclaw://settings"))
            {
                Title = "⚙️ Settings",
                Subtitle = "Configure OpenClaw Tray"
            }
        ];
    }
}

