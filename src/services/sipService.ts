import { UserAgent, UserAgentOptions, Registerer, Inviter, SessionState, Invitation, Session } from 'sip.js';
import { useAppStore } from '../stores/appState';

export class SipService {
  private userAgent: UserAgent | null = null;
  private registerer: Registerer | null = null;
  private currentSession: Session | null = null;
  private store = useAppStore();
  private callTimer: number | null = null;

  async initialize(options: UserAgentOptions) {
    this.userAgent = new UserAgent(options);

    this.userAgent.delegate = {
      onInvite: (invitation: Invitation) => {
        this.handleIncomingInvite(invitation);
      }
    };

    await this.userAgent.start();
    this.registerer = new Registerer(this.userAgent);
    await this.registerer.register();
    this.store.setCallState('registered');
  }

  private handleIncomingInvite(invitation: Invitation) {
    this.currentSession = invitation;
    this.store.remoteNumber = invitation.remoteIdentity.uri.user || 'Unknown';
    this.store.setCallState('incoming');

    invitation.stateChange.addListener((state) => {
      this.handleSessionStateChange(state);
    });
  }

  async makeCall(target: string) {
    if (!this.userAgent) return;

    const targetURI = UserAgent.makeURI(`sip:${target}@${this.userAgent.configuration.uri.host}`);
    if (!targetURI) return;

    const inviter = new Inviter(this.userAgent, targetURI);
    this.currentSession = inviter;

    inviter.stateChange.addListener((state) => {
      this.handleSessionStateChange(state);
    });

    await inviter.invite();
    this.store.setCallState('outgoing');
  }

  private handleSessionStateChange(state: SessionState) {
    switch (state) {
      case SessionState.Establishing:
        this.store.setCallState('ringing');
        break;
      case SessionState.Established:
        this.store.setCallState('in-call');
        this.startCallTimer();
        break;
      case SessionState.Terminating:
      case SessionState.Terminated:
        this.store.setCallState('registered');
        this.stopCallTimer();
        this.currentSession = null;
        break;
    }
  }

  async answer() {
    if (this.currentSession instanceof Invitation) {
      await this.currentSession.accept();
    }
  }

  async hangup() {
    if (this.currentSession) {
      if (this.currentSession instanceof Invitation && this.currentSession.state === SessionState.Initial) {
        await this.currentSession.reject();
      } else {
        await this.currentSession.bye();
      }
    }
  }

  async hold() {
    // Basic implementation
    if (this.currentSession) {
      // SIP.js hold logic here
      this.store.isOnHold = true;
    }
  }

  async unhold() {
    if (this.currentSession) {
      this.store.isOnHold = false;
    }
  }

  private startCallTimer() {
    this.store.callDuration = 0;
    this.callTimer = window.setInterval(() => {
      this.store.callDuration++;
    }, 1000);
  }

  private stopCallTimer() {
    if (this.callTimer) {
      clearInterval(this.callTimer);
      this.callTimer = null;
    }
  }
}

export const sipService = new SipService();
