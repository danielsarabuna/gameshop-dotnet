import React from 'react';
import { BrowserRouter, Routes, Route, useLocation } from 'react-router-dom';
import { LanguageProvider } from './context/LanguageContext';
import { CartProvider } from './context/CartContext';
import { AuthProvider } from './context/AuthContext';
import { Header } from './components/Header';
import { Footer } from './components/Footer';
import { CartDrawer } from './components/CartDrawer';
import { LoginModal, ProfileModal, AuthNotice, DeeplinkToast } from './components/Modals';
import { Home } from './pages/Home';
import { Projects } from './pages/Projects';
import { Diamonds } from './pages/Diamonds';
import { Subscription } from './pages/Subscription';
import { Help } from './pages/Help';
import { About } from './pages/About';
import { PaymentResultModal, PaymentReturn } from './pages/OrderResult';
import { MockCheckout } from './pages/MockCheckout';
import { clearPaymentResult, readPaymentResult, savePaymentResult, StoredPaymentResult } from './services/paymentSession';

const HIDDEN_FOOTER_PATHS = ['/', '/projects'];

const ScrollToTop: React.FC = () => {
  const { pathname } = useLocation();

  React.useEffect(() => {
    window.scrollTo(0, 0);
    document.documentElement.scrollTop = 0;
    document.body.scrollTop = 0;
    const stageContainer = document.querySelector('.stage-center-container');
    if (stageContainer) stageContainer.scrollTop = 0;
    const snapContainers = document.querySelectorAll(
      '.hero-snap-container, .game-showcase-container, .diamonds-snap-container, .sub-snap-container'
    );
    snapContainers.forEach((el) => (el.scrollTop = 0));
  }, [pathname]);

  return null;
};

const Shell: React.FC = () => {
  const { pathname } = useLocation();
  const showFooter = !HIDDEN_FOOTER_PATHS.includes(pathname);
  const [paymentResult, setPaymentResult] = React.useState<StoredPaymentResult | null>(() => readPaymentResult());
  const showPaymentResult = React.useCallback((result: StoredPaymentResult) => {
    savePaymentResult(result);
    setPaymentResult(result);
  }, []);
  const closePaymentResult = React.useCallback(() => {
    clearPaymentResult();
    setPaymentResult(null);
  }, []);

  return (
    <>
      <ScrollToTop />
      <div className="app-viewport-wrapper">
        <div className="side-gradient-vignette" />
        <Header />
        <div className="stage-center-container">
          <main style={{ minHeight: 'calc(100vh - 80px)' }}>
            <Routes>
              <Route path="/" element={<Home />} />
              <Route path="/projects" element={<Projects />} />
              <Route path="/diamonds" element={<Diamonds />} />
              <Route path="/subscription" element={<Subscription />} />
              <Route path="/help" element={<Help />} />
              <Route path="/about" element={<About />} />
              <Route path="/order/complete" element={<PaymentReturn outcome="complete" onResult={showPaymentResult} />} />
              <Route path="/order/cancelled" element={<PaymentReturn outcome="cancelled" onResult={showPaymentResult} />} />
              <Route path="/order/failed" element={<PaymentReturn outcome="failed" onResult={showPaymentResult} />} />
              <Route path="/order/mock-checkout" element={<MockCheckout />} />
            </Routes>
          </main>
          {showFooter && <Footer />}
        </div>
        {/* Drawer & Modals at viewport root to prevent z-index clipping */}
        <CartDrawer onPaymentResult={showPaymentResult} />
        <LoginModal />
        <ProfileModal />
        <AuthNotice />
        <DeeplinkToast />
        {paymentResult && <PaymentResultModal key={`${paymentResult.outcome}:${paymentResult.orderId}`} result={paymentResult} onClose={closePaymentResult} />}
      </div>
    </>
  );
};

export const App: React.FC = () => {
  return (
    <BrowserRouter>
      <LanguageProvider>
        <AuthProvider>
          <CartProvider>
            <Shell />
          </CartProvider>
        </AuthProvider>
      </LanguageProvider>
    </BrowserRouter>
  );
};
