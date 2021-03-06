import { Component, OnInit } from '@angular/core';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';

class HandshakeMessage {
  public email = '';
  public param2  = '';
}


@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss']
})
export class AppComponent implements OnInit {
  title = 'MarketDashboard';
  version = '0.1.1';
  user = {
    name: 'Anonymous',
    email: 'anonymous@gmail.com'
  };
  isToolSelectionVisible = false;
  isUserSelectionVisible = false;
  toolSelectionMsg = 'Click red arrow in toolbar! isToolSelectionVisible is set to ' + this.isToolSelectionVisible;
  activeTool = 'MarketHealth';

  // http://localhost:4202/hub/exsvpush/negotiate?negotiateVersion=1 404 (Not Found), if it is not served on port 4202 on ng serve (proxy)
  public _hubConnection: HubConnection = new HubConnectionBuilder().withUrl('/hub/dashboardpush').build();

  // called after Angular has initialized all data-bound properties before any of the view or content children have been checked. Handle any additional initialization tasks.
  ngOnInit() {
    console.log('Sq: ngOnInit()');
    this.onSetTheme('light');

    this._hubConnection
      .start()
      .then(() => {
        console.log('ws: Connection started!');
        this._hubConnection.send('startStreaming', 'message body')  // Error: Cannot send data if the connection is not in the 'Connected' State.
          .then(() => console.log('ws: Connection sent message!'));
      })
      .catch(err => console.log('Error while establishing connection :('));

    this._hubConnection.on('OnConnected', (message: HandshakeMessage) => {
      console.log('ws: OnConnected Message arrived:' + message.email);
      this.user.email = message.email;
    });
  }

  onSetTheme(theme: string) {
    let bgColor = '';
    let textColor = '';
    switch (theme) {
      case 'light':
        bgColor = '#ffffff';
        textColor = '#000000';
        break;
      case 'dark':
        bgColor = '#0000ff';
        textColor = '#aaaaaa';
        break;
    }
    document.body.style.setProperty('--bgColor', bgColor);
    document.body.style.setProperty('--textColor', textColor);
    console.log('Sq: set theme.');
  }

  onClickToolSelection() {
    this.isToolSelectionVisible = !this.isToolSelectionVisible;
    this.toolSelectionMsg = 'Click red arrow in toolbar! isToolSelectionVisible is set to ' + this.isToolSelectionVisible;
    this.isUserSelectionVisible = false;
  }

  onClickToolSelected() {
    this.isToolSelectionVisible = !this.isToolSelectionVisible;
    this.toolSelectionMsg = 'Click red arrow in toolbar! isToolSelectionVisible is set to ' + this.isToolSelectionVisible;
  }

  onClickUserSelection() {
    this.isUserSelectionVisible = !this.isUserSelectionVisible;
    this.isToolSelectionVisible = false;
  }

  onClickUserSelected() {
    this.isUserSelectionVisible = !this.isUserSelectionVisible;
  }

  // input comes from HTML. such as 'Docs-WhatIsNew'
  onChangeActiveTool(tool: string) {
    if (this.activeTool === tool) {
      return;
    }
    this.activeTool = tool;
  }

  closeDropdownMenu(menuItem: string) {
    if (menuItem === 'Tools') {
      this.isToolSelectionVisible = false;
    } else if (menuItem === 'User') {
      this.isUserSelectionVisible = false;
    }
  }

}
