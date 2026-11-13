import { Component } from 'react';

// Keeps a step that throws while rendering from taking the whole wizard down: the draft stays, and the user can try
// the step again or move to another one.
class WizardStepBoundary extends Component {
  constructor(props) {
    super(props);
    this.state = { error: null };
  }

  static getDerivedStateFromError(error) {
    return { error };
  }

  render() {
    const { t, children } = this.props;
    if (!this.state.error) return children;
    return (
      <div className="sw-failure" role="alert">
        <strong>{t('wizard.failure.renderTitle')}</strong>
        <p className="sw-failure-message">{String(this.state.error?.message || this.state.error)}</p>
        <div className="sw-row">
          <button type="button" className="sw-btn sw-btn-primary" onClick={() => this.setState({ error: null })}>{t('wizard.failure.retry')}</button>
        </div>
      </div>
    );
  }
}

export default WizardStepBoundary;
